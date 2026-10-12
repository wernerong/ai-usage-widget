'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');

function widgetFolder(platform = process.platform, env = process.env, home = os.homedir()) {
  if (platform === 'darwin') return path.join(home, 'Library', 'Application Support', 'AIUsageWidget');
  if (platform === 'win32' && env.LOCALAPPDATA) return path.join(env.LOCALAPPDATA, 'AIUsageWidget');
  throw new Error('The desktop widget connection supports Windows and macOS.');
}
function hostForProvider(provider, enterpriseUri) {
  if (provider === 'github') return 'github.com';
  const uri = new URL(enterpriseUri || '');
  if (provider !== 'github-enterprise' || uri.protocol !== 'https:' || uri.username || uri.password || uri.port ||
      uri.search || uri.hash || (uri.pathname !== '/' && uri.pathname !== '') ||
      !/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.ghe\.com$/i.test(uri.hostname))
    throw new Error('Use GitHub.com or your configured GHE.com account. Enterprise Server is not supported.');
  return uri.hostname.toLowerCase();
}
function endpoint(host) {
  if (host !== 'github.com' && !/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.ghe\.com$/.test(host)) throw new Error('Unsupported GitHub host.');
  return `https://${host === 'github.com' ? 'api.github.com' : `api.${host}`}/copilot_internal/user`;
}
function number(value) {
  if ((typeof value !== 'number' && typeof value !== 'string') || value === '' || String(value).trim() === '') return undefined;
  const n = Number(value);
  return Number.isFinite(n) && n >= 0 && n <= 1e15 ? n : undefined;
}
function date(value) {
  if (typeof value !== 'number' && typeof value !== 'string') return undefined;
  const d = new Date(typeof value === 'number' ? value * (value < 1e12 ? 1000 : 1) : value);
  return Number.isFinite(d.getTime()) && d.getTime() > 0 ? d.toISOString() : undefined;
}
function sanitize(root) {
  if (!root || typeof root !== 'object' || Array.isArray(root)) throw new Error('invalid_data');
  const data = {};
  if (typeof root.token_based_billing === 'boolean') data.token_based_billing = root.token_based_billing;
  if (['free', 'individual', 'individual_pro', 'individual_pro_plus', 'business', 'enterprise', 'pro', 'pro_plus', 'max'].includes(root.copilot_plan)) data.copilot_plan = root.copilot_plan;
  for (const key of ['quota_reset_date_utc', 'quota_reset_date', 'limited_user_reset_date']) {
    const value = date(root[key]); if (value !== undefined) data[key] = value;
  }
  for (const key of ['monthly_quotas', 'limited_user_quotas']) {
    if (!root[key] || typeof root[key] !== 'object') continue;
    data[key] = {};
    for (const name of ['premium_interactions', 'chat', 'completions']) {
      const value = number(root[key][name]); if (value !== undefined) data[key][name] = value;
    }
  }
  if (root.quota_snapshots && typeof root.quota_snapshots === 'object') {
    data.quota_snapshots = {};
    for (const name of ['premium_interactions', 'chat', 'completions']) {
      const raw = root.quota_snapshots[name];
      if (!raw || typeof raw !== 'object' || Array.isArray(raw)) continue;
      const item = {};
      for (const key of ['unlimited', 'has_quota', 'overage_permitted', 'token_based_billing'])
        if (typeof raw[key] === 'boolean') item[key] = raw[key];
      for (const key of ['percent_remaining', 'entitlement', 'quota_remaining', 'credits_used', 'overage_count', 'overage_entitlement']) {
        const value = number(raw[key]);
        if (value !== undefined && (key !== 'percent_remaining' || value <= 100)) item[key] = value;
      }
      const reset = date(raw.quota_reset_at); if (reset !== undefined) item.quota_reset_at = reset;
      if (Object.keys(item).length) data.quota_snapshots[name] = item;
    }
  }
  if (!Object.keys(data.quota_snapshots || {}).length && !Object.keys(data.limited_user_quotas || {}).length) throw new Error('invalid_data');
  return data;
}
async function atomicJson(file, data) {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const temp = `${file}.${crypto.randomUUID()}.tmp`;
  try {
    await fs.writeFile(temp, JSON.stringify(data), { mode: 0o600 });
    // Windows readers and antivirus can briefly hold the destination without
    // delete sharing. Keep the existing snapshot until an atomic rename works.
    for (let attempt = 0; ; attempt++) {
      try { await fs.rename(temp, file); break; }
      catch (error) {
        if (attempt >= 5 || !['EPERM', 'EACCES', 'EBUSY'].includes(error.code)) throw error;
        await new Promise(resolve => setTimeout(resolve, 25 * 2 ** attempt));
      }
    }
  } finally { await fs.unlink(temp).catch(() => {}); }
}
async function readJson(file) {
  try { return JSON.parse(await fs.readFile(file, 'utf8')); } catch { return undefined; }
}
function retryTime(header, now = Date.now()) {
  const seconds = Number(header);
  const delay = header && Number.isFinite(seconds) ? seconds * 1000 : Date.parse(header) - now;
  return now + Math.min(86400000, Math.max(60000, Number.isFinite(delay) ? delay : 300000));
}
module.exports = { widgetFolder, hostForProvider, endpoint, number, date, sanitize, atomicJson, readJson, retryTime };
