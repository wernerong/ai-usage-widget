'use strict';
const path = require('node:path');
const crypto = require('node:crypto');
const usage = require('./usage');

const scopes = [['read:user'], ['user:email'], ['read:user', 'user:email', 'repo', 'workflow']];
const errors = new Set(['signed_out', 'unauthorized', 'forbidden', 'rate_limited', 'invalid_data', 'service_unavailable', 'disconnected']);
class Connection {
  constructor(vscode, context, folder, fetcher = fetch) {
    this.vscode = vscode; this.context = context; this.folder = folder; this.fetcher = fetcher;
    this.configPath = path.join(folder, 'copilot-connection.json');
    this.snapshotPath = path.join(folder, 'copilot-vscode-usage.json');
    this.busy = false; this.alive = true; this.next = 0; this.controller = undefined;
  }
  async owned(connection) {
    const config = await usage.readJson(this.configPath);
    return this.alive && config?.Schema === 1 && config.Mode === 'vscode' && config.Id === connection.id;
  }
  async connect(pairingId) {
    if (this.busy) return false;
    this.busy = true;
    try {
      const accounts = [];
      for (const provider of ['github', 'github-enterprise']) {
        let host;
        try { host = usage.hostForProvider(provider, this.vscode.workspace.getConfiguration('github-enterprise').get('uri')); }
        catch { continue; }
        for (const account of await this.vscode.authentication.getAccounts(provider).catch(() => []))
          accounts.push({ label: account.label, description: host, provider, host, account });
      }
      let selected = accounts.length ? await this.vscode.window.showQuickPick(accounts, { title: 'Choose the GitHub account with your Copilot subscription', placeHolder: 'Use the same work account as Copilot in this VS Code profile' }) : undefined;
      if (!selected && accounts.length) return false;
      // With no existing account, the standard VS Code GitHub provider handles
      // sign-in and consent. We never inspect another extension's secret store.
      if (!selected) selected = { provider: 'github', host: 'github.com', account: undefined };
      let session;
      for (const desired of scopes) {
        session = await this.vscode.authentication.getSession(selected.provider, desired, { silent: true, account: selected.account });
        if (session) { selected.scopes = desired; break; }
      }
      if (!session) {
        selected.scopes = scopes[0];
        session = await this.vscode.authentication.getSession(selected.provider, scopes[0], { createIfNone: true, account: selected.account });
      }
      if (!session || (selected.account && session.account.id !== selected.account.id)) throw new Error('signed_out');
      const config = await usage.readJson(this.configPath);
      if (pairingId && (config?.Schema !== 1 || config.Mode !== 'vscode' || config.Id !== pairingId)) throw new Error('disconnected');
      const connection = { id: pairingId || crypto.randomUUID(), provider: selected.provider, host: selected.host, scopes: selected.scopes, account: { id: session.account.id, label: session.account.label } };
      await this.context.globalState.update('connection', connection);
      await usage.atomicJson(this.configPath, { Schema: 1, Mode: 'vscode', Id: connection.id });
      this.next = 0;
    } finally { this.busy = false; }
    return this.refresh(true);
  }
  async writeError(connection, code) {
    if (await this.owned(connection)) await usage.atomicJson(this.snapshotPath, { Schema: 1, ConnectionId: connection.id, Error: errors.has(code) ? code : 'service_unavailable' });
  }
  async refresh(manual = false) {
    const connection = this.context.globalState.get('connection');
    if (!connection || this.busy || !await this.owned(connection)) return false;
    // Manual refresh also respects server backoff.
    if (Date.now() < this.next) return false;
    this.busy = true;
    this.controller = new AbortController();
    const timeout = setTimeout(() => this.controller?.abort(), 25000);
    try {
      const session = await this.vscode.authentication.getSession(connection.provider, connection.scopes, { silent: true, account: connection.account });
      if (!session || session.account.id !== connection.account.id) throw new Error('signed_out');
      const response = await this.fetcher(usage.endpoint(connection.host), { redirect: 'error', signal: this.controller.signal, headers: { Authorization: `Bearer ${session.accessToken}`, Accept: 'application/json', 'User-Agent': 'AIUsageWidget-VSCode', 'X-GitHub-Api-Version': '2022-11-28' } });
      if (response.status === 429) { this.next = usage.retryTime(response.headers.get('retry-after')); throw new Error('rate_limited'); }
      if (response.status === 401) throw new Error('unauthorized');
      if (response.status === 403) throw new Error('forbidden');
      if (!response.ok) throw new Error('service_unavailable');
      // Whitelist the usage fields. Account emails, endpoints, org memberships,
      // access/refresh tokens, and the original response are never persisted.
      const data = usage.sanitize(await response.json());
      if (!await this.owned(connection)) return false;
      await usage.atomicJson(this.snapshotPath, { Schema: 1, ConnectionId: connection.id, Account: session.account.label.replace(/[\r\n\t]/g, '').slice(0, 100), Host: connection.host, Fetched: new Date().toISOString(), Usage: data });
      return true;
    } catch (error) {
      await this.writeError(connection, errors.has(error?.message) ? error.message : 'service_unavailable').catch(() => {});
      if (manual) await this.vscode.window.showWarningMessage('Copilot usage could not be shared. Check your GitHub sign-in and company access policy, then try again. No usage was estimated.');
      return false;
    } finally { clearTimeout(timeout); this.controller = undefined; this.busy = false; }
  }
  async disconnect() {
    const connection = this.context.globalState.get('connection');
    this.controller?.abort();
    // Change ownership first so an older in-flight response cannot restore data.
    if (connection && await this.owned(connection)) {
      await usage.atomicJson(this.configPath, { Schema: 1, Mode: 'vscode', Id: crypto.randomUUID() });
      await usage.atomicJson(this.snapshotPath, { Schema: 1, Error: 'disconnected' });
    }
    await this.context.globalState.update('connection', undefined);
  }
  dispose() { this.alive = false; this.controller?.abort(); }
}
module.exports = { Connection };
