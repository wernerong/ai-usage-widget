'use strict';
const path = require('node:path');
const usage = require('./usage');
const version = require('./package.json').version;

const stages = new Set(['accounts', 'sign_in', 'pairing', 'save', 'refresh', 'complete']);
const codes = new Set(['started', 'ok', 'cancelled', 'consent_required', 'provider_unavailable', 'account_mismatch', 'disconnected', 'storage_denied', 'storage_failed', 'signed_out', 'unauthorized', 'forbidden', 'rate_limited', 'invalid_data', 'service_unavailable', 'setup_failed']);
function classify(error) {
  if (codes.has(error?.message)) return error.message;
  if (error?.code === 'EACCES' || error?.code === 'EPERM') return 'storage_denied';
  if (/^User did not consent to login\.?$/i.test(error?.message || '')) return 'consent_required';
  if (/^(Cancelled|Canceled|CancellationError)$/i.test(error?.name || '') || /^(Cancelled|Canceled)$/i.test(error?.message || error || '')) return 'cancelled';
  if (/authentication provider.*(not found|not registered)|Unable to find authentication provider|No authentication provider/i.test(error?.message || '')) return 'provider_unavailable';
  return 'setup_failed';
}
function message(error) {
  const code = classify(error);
  const texts = {
    consent_required: 'GitHub access was not approved. Run AI Usage Widget: Connect Copilot Account again and choose Allow in the VS Code GitHub access prompt.',
    cancelled: 'Copilot connection was cancelled. Run AI Usage Widget: Connect Copilot Account when you are ready.',
    provider_unavailable: 'VS Code GitHub Authentication is unavailable. Enable the built-in GitHub Authentication extension, reload VS Code, and reconnect.',
    account_mismatch: 'GitHub returned a different account. Reconnect and select the same work account that Copilot uses in this VS Code profile.',
    disconnected: 'The widget connection changed during setup. Run AI Usage Widget: Connect Copilot Account again to connect the current selection.',
    storage_denied: 'The Copilot connection could not save its local settings. Check access to your AIUsageWidget data folder and reconnect.',
    storage_failed: 'VS Code could not save the Copilot connection. Reload VS Code and reconnect.'
  };
  return texts[code] || `Copilot connection failed at ${stages.has(error?.stage) ? error.stage : 'setup'} (${code}). Choose Show details for the connection log.`;
}
class SetupError extends Error {
  constructor(stage, error) {
    const code = classify(error);
    super(code === 'setup_failed' && stage === 'save' ? 'storage_failed' : code);
    this.stage = stages.has(stage) ? stage : 'sign_in';
  }
}
async function record(folder, stage, code, log = () => {}) {
  // Never record the original exception, stack, account, pairing ID, token, or response.
  const safeStage = stages.has(stage) ? stage : 'sign_in';
  const safeCode = codes.has(code) ? code : 'setup_failed';
  const data = { Schema: 1, Version: version, Stage: safeStage, Code: safeCode, Updated: new Date().toISOString() };
  log(`${version} ${safeStage}: ${safeCode}`);
  try { await usage.atomicJson(path.join(folder, 'copilot-vscode-diagnostics.json'), data); } catch { /* Diagnostic failure must not block authentication. */ }
}
module.exports = { classify, message, SetupError, record };
