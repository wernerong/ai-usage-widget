'use strict';
const vscode = require('vscode');
const { Connection } = require('./connection');
const { widgetFolder } = require('./usage');

function activate(context) {
  let folder;
  try { folder = widgetFolder(); } catch { return; }
  const connection = new Connection(vscode, context, folder);
  async function connect(id) {
    try {
      if (await connection.connect(id)) await vscode.window.showInformationMessage('Copilot usage is connected to AI Usage Widget. Keep VS Code open for live updates.');
    } catch { await vscode.window.showWarningMessage('Copilot connection was not completed. Choose your work GitHub account and approve access in VS Code, then try again.'); }
  }
  const timer = setInterval(() => { connection.refresh().catch(() => {}); }, 60000);
  context.subscriptions.push(
    vscode.commands.registerCommand('aiUsageWidget.connectCopilot', () => connect()),
    vscode.commands.registerCommand('aiUsageWidget.refreshCopilot', () => connection.refresh(true)),
    vscode.commands.registerCommand('aiUsageWidget.disconnectCopilot', () => connection.disconnect()),
    vscode.window.registerUriHandler({ handleUri(uri) { if (uri.path === '/connect') { const id = new URLSearchParams(uri.query).get('id'); if (id && /^[0-9a-f-]{36}$/i.test(id)) return connect(id); } } }),
    vscode.authentication.onDidChangeSessions(() => { connection.refresh().catch(() => {}); }),
    { dispose() { clearInterval(timer); connection.dispose(); } }
  );
  connection.refresh().catch(() => {});
}
module.exports = { activate };
