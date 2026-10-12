'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { Connection } = require('../connection');
const usage = require('../usage');
const diagnostics = require('../diagnostics');

test('Bundled extension and widget releases use the same version', async () => {
  const project = await fs.readFile(path.join(__dirname, '..', '..', 'source', 'UsageWidget.csproj'), 'utf8');
  assert.equal(require('../package.json').version, project.match(/<Version>([^<]+)<\/Version>/)[1]);
});

test('Whitelisted quotas preserve precise usage and exclude all credential/account payloads', () => {
  const data = usage.sanitize({ token_based_billing: true, copilot_plan: 'business', access_token: 'private-fixture', email: 'private-fixture', organization_login_list: ['private-fixture'], endpoints: { private: 'private-fixture' }, quota_snapshots: { premium_interactions: { percent_remaining: 64.6, entitlement: '1000', credits_used: 97, has_quota: false, unlimited: false, quota_reset_at: 1793491200, refresh_token: 'private-fixture' }, unknown: { secret: 'private-fixture' } } });
  assert.equal(data.quota_snapshots.premium_interactions.percent_remaining, 64.6);
  assert.equal(data.quota_snapshots.premium_interactions.entitlement, 1000);
  assert.equal(data.quota_snapshots.premium_interactions.has_quota, false);
  assert.equal(data.quota_snapshots.premium_interactions.quota_reset_at, '2026-11-01T00:00:00.000Z');
  assert.ok(!JSON.stringify(data).includes('private-fixture'));
});
test('Invalid numeric sentinels never turn into zero usage', () => {
  const data = usage.sanitize({ quota_snapshots: { premium_interactions: { percent_remaining: -1, credits_used: Infinity, entitlement: '', unlimited: true } } });
  assert.deepEqual(data.quota_snapshots.premium_interactions, { unlimited: true });
  assert.throws(() => usage.sanitize({ email: 'ignored' }), /invalid_data/);
});
test('Token origins are restricted to GitHub and the explicitly configured GHE.com host', () => {
  assert.equal(usage.endpoint(usage.hostForProvider('github')), 'https://api.github.com/copilot_internal/user');
  assert.equal(usage.endpoint(usage.hostForProvider('github-enterprise', 'https://example.ghe.com')), 'https://api.example.ghe.com/copilot_internal/user');
  for (const uri of ['http://example.ghe.com', 'https://example.ghe.com.evil.test', 'https://example.ghe.com:8443', 'https://u:p@example.ghe.com', 'https://example.ghe.com/path', 'https://github.company.test'])
    assert.throws(() => usage.hostForProvider('github-enterprise', uri));
});
test('Platform data paths match the native widget and unsupported systems fail explicitly', () => {
  assert.equal(usage.widgetFolder('win32', { LOCALAPPDATA: '/local' }), path.join('/local', 'AIUsageWidget'));
  assert.equal(usage.widgetFolder('darwin', {}, '/home'), path.join('/home', 'Library', 'Application Support', 'AIUsageWidget'));
  assert.throws(() => usage.widgetFolder('linux', {}, '/home'));
});
async function fixture(t) {
  const folder = await fs.mkdtemp(path.join(os.tmpdir(), 'widget-copilot-check-'));
  t.after(() => fs.rm(folder, { recursive: true, force: true }));
  const values = new Map(); const calls = []; const messages = [];
  const session = { accessToken: 'never-persist-this-fixture', account: { id: 'work-fixture', label: 'work-example' } };
  const vscode = {
    workspace: { getConfiguration: () => ({ get: () => undefined }) },
    authentication: { getAccounts: async provider => provider === 'github' ? [session.account] : [], getSession: async (_p, _s, options) => { calls.push(options); return session; } },
    window: { showQuickPick: async accounts => accounts[0], showWarningMessage: async msg => messages.push(msg) }
  };
  const context = { globalState: { get: key => values.get(key), update: async (key, value) => values.set(key, value) } };
  const body = { token_based_billing: true, copilot_plan: 'business', quota_snapshots: { premium_interactions: { percent_remaining: 64.6, unlimited: false, entitlement: 1000, credits_used: 97 } }, access_token: session.accessToken };
  let count = 0;
  const controller = new Connection(vscode, context, folder, async (url, options) => {
    count++; assert.equal(url, 'https://api.github.com/copilot_internal/user');
    assert.equal(options.headers.Authorization, 'Bearer ' + session.accessToken);
    assert.equal(options.redirect, 'error');
    return { ok: true, status: 200, json: async () => body };
  });
  t.after(() => controller.dispose());
  return { folder, session, controller, vscode, context, calls, messages, count: () => count };
}
test('Approved work-account refresh writes only usage, and existing auth is requested silently', async t => {
  const f = await fixture(t);
  assert.equal(await f.controller.connect(), true);
  assert.equal(f.count(), 1);
  const snapshot = await usage.readJson(f.controller.snapshotPath);
  assert.equal(snapshot.Usage.quota_snapshots.premium_interactions.percent_remaining, 64.6);
  assert.equal(snapshot.Account, 'work-example');
  assert.ok(!JSON.stringify(snapshot).includes(f.session.accessToken));
  assert.ok(!JSON.stringify(f.context.globalState.get('connection')).includes(f.session.accessToken));
  assert.ok(f.calls.every(call => call.silent && call.account.id === f.session.account.id));
});
test('Explicit source selection prevents another profile or CLI switch from writing old data', async t => {
  const f = await fixture(t); await f.controller.connect();
  await usage.atomicJson(f.controller.configPath, { Schema: 1, Mode: 'direct', Id: 'another-selection' });
  assert.equal(await f.controller.refresh(), false);
  assert.equal(f.count(), 1);
  await f.controller.disconnect();
  assert.equal((await usage.readJson(f.controller.configPath)).Mode, 'direct');
});
test('Signed-out accounts are never silently replaced by a different available account', async t => {
  const f = await fixture(t); await f.controller.connect();
  f.vscode.authentication.getSession = async () => ({ ...f.session, account: { id: 'personal-fixture', label: 'personal-example' } });
  assert.equal(await f.controller.refresh(), false);
  assert.equal(f.count(), 1);
  assert.equal((await usage.readJson(f.controller.snapshotPath)).Error, 'signed_out');
});
test('Rate limiting invalidates freshness and manual refresh cannot bypass server backoff', async t => {
  const f = await fixture(t); await f.controller.connect();
  let requests = 0;
  f.controller.fetcher = async () => { requests++; return { status: 429, headers: { get: () => '600' } }; };
  assert.equal(await f.controller.refresh(), false);
  assert.equal((await usage.readJson(f.controller.snapshotPath)).Error, 'rate_limited');
  assert.equal(await f.controller.refresh(true), false);
  assert.equal(requests, 1);
});
test('A disconnect during an in-flight request cannot resurrect usage or access tokens', async t => {
  const f = await fixture(t); await f.controller.connect();
  let finish; const waiting = new Promise(resolve => { finish = resolve; });
  let started; const entered = new Promise(resolve => { started = resolve; });
  f.controller.fetcher = async () => { started(); await waiting; return { ok: true, status: 200, json: async () => ({ quota_snapshots: { premium_interactions: { percent_remaining: 99 } } }) }; };
  const pending = f.controller.refresh(); await entered;
  await f.controller.disconnect(); finish(); await pending;
  assert.equal((await usage.readJson(f.controller.snapshotPath)).Error, 'disconnected');
  assert.equal(f.context.globalState.get('connection'), undefined);
});

test('A passive scope failure still reaches explicit read-only consent and never records its raw error', async t => {
  const f = await fixture(t);
  let approved = false;
  f.vscode.authentication.getSession = async (_provider, requested, options) => {
    f.calls.push(options);
    if (approved) return f.session;
    if (options.silent) throw new Error('private-error-fixture ' + f.session.accessToken);
    assert.deepEqual(requested, ['read:user']);
    assert.ok(options.createIfNone.detail.includes('only usage values'));
    assert.equal(options.silent, undefined);
    approved = true;
    return f.session;
  };
  assert.equal(await f.controller.connect(), true);
  assert.equal(f.count(), 1);
  const records = await Promise.all((await fs.readdir(f.folder)).map(file => fs.readFile(path.join(f.folder, file), 'utf8')));
  assert.ok(records.every(record => !record.includes('private-error-fixture') && !record.includes(f.session.accessToken)));
});

test('Declined VS Code consent reports the specific next action without requesting again', async t => {
  const f = await fixture(t);
  let prompts = 0;
  f.vscode.authentication.getSession = async (_provider, _scopes, options) => {
    if (options.silent) return undefined;
    prompts++; throw new Error('User did not consent to login.');
  };
  await assert.rejects(f.controller.connect(), error => error.message === 'consent_required' && error.stage === 'sign_in' && diagnostics.message(error).includes('choose Allow'));
  assert.equal(prompts, 1);
  assert.equal(f.count(), 0);
  assert.equal((await usage.readJson(path.join(f.folder, 'copilot-vscode-diagnostics.json'))).Code, 'consent_required');
  assert.equal(await usage.readJson(f.controller.snapshotPath), undefined);
});

test('A stale URI fails before asking for account access', async t => {
  const f = await fixture(t);
  const currentId = require('node:crypto').randomUUID();
  await usage.atomicJson(f.controller.configPath, { Schema: 1, Mode: 'vscode', Id: currentId });
  await assert.rejects(f.controller.connect(require('node:crypto').randomUUID()), /disconnected/);
  assert.equal(f.calls.length, 0);
  assert.equal(f.count(), 0);
  assert.equal((await usage.readJson(f.controller.configPath)).Id, currentId);
});

test('Command Palette retry shares the widget pairing and account mismatch remains an error', async t => {
  const f = await fixture(t);
  const currentId = require('node:crypto').randomUUID();
  await usage.atomicJson(f.controller.configPath, { Schema: 1, Mode: 'vscode', Id: currentId });
  assert.equal(await f.controller.connect(), true);
  assert.equal((await usage.readJson(f.controller.snapshotPath)).ConnectionId, currentId);
  f.vscode.authentication.getSession = async () => ({ ...f.session, account: { id: 'other-account', label: 'other-example' } });
  await assert.rejects(f.controller.connect(), /account_mismatch/);
  assert.equal(f.count(), 1);
});

test('Changing the widget source during approval prevents saving the old connection', async t => {
  const f = await fixture(t);
  const currentId = require('node:crypto').randomUUID();
  await usage.atomicJson(f.controller.configPath, { Schema: 1, Mode: 'vscode', Id: currentId });
  f.vscode.authentication.getSession = async () => {
    await usage.atomicJson(f.controller.configPath, { Schema: 1, Mode: 'direct', Id: require('node:crypto').randomUUID() });
    return f.session;
  };
  await assert.rejects(f.controller.connect(currentId), /disconnected/);
  assert.equal(f.context.globalState.get('connection'), undefined);
  assert.equal(f.count(), 0);
  assert.equal((await usage.readJson(f.controller.configPath)).Mode, 'direct');
});

test('Local diagnostics contain only a safe stage, code, version and time', async t => {
  const f = await fixture(t);
  const logs = [];
  await diagnostics.record(f.folder, 'private-stage-fixture', f.session.accessToken, line => logs.push(line));
  const data = await usage.readJson(path.join(f.folder, 'copilot-vscode-diagnostics.json'));
  assert.deepEqual(Object.keys(data).sort(), ['Code', 'Schema', 'Stage', 'Updated', 'Version']);
  assert.equal(data.Stage, 'sign_in'); assert.equal(data.Code, 'setup_failed');
  assert.ok(!JSON.stringify([data, logs]).includes(f.session.accessToken));
  assert.ok(!JSON.stringify([data, logs]).includes('private-stage-fixture'));
  assert.equal(diagnostics.classify({ code: 'EACCES', message: f.session.accessToken }), 'storage_denied');
  assert.equal(new diagnostics.SetupError('save', new Error(f.session.accessToken)).message, 'storage_failed');
});
