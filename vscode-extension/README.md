# AI Usage Widget — Copilot Connection

Share your Copilot usage with the floating [AI Usage Widget](https://github.com/wernerong/ai-usage-widget) on Windows or macOS.

Since v1.14.2, **Connections → Copilot → Connect GitHub account** in the widget is the recommended setup. It reads GitHub directly through Git Credential Manager and works with the editor closed. This companion remains available for existing connections; choosing the direct GitHub source stops its sharing.

For a companion connection, install the bundled VSIX from a GitHub release, then run **AI Usage Widget: Connect Copilot Account** in VS Code. Select the GitHub account with your Copilot subscription and approve the standard VS Code authentication request once. No token needs to be copied into the widget.

You can also install the VSIX from the GitHub release through **Extensions → … → Install from VSIX…**, then run **AI Usage Widget: Connect Copilot Account** from the Command Palette and enable **Subscriptions → Copilot** in the widget. Select the same work account as Copilot in this VS Code profile. GitHub.com and configured GitHub Enterprise Cloud accounts on GHE.com are supported; arbitrary Enterprise Server hosts are not.

While VS Code is open, the extension refreshes about once per minute. The widget displays GitHub's reported percentage with no decimals. Business/Enterprise accounts with no personal cap show **No cap** and credits used in the details; an exhausted company pool shows **Empty**. The percentage is never estimated from an assumed plan allowance or by dividing credits used by the nominal subscription credits. Additional allowances are reported separately. Legacy premium-request and Free chat allowances remain supported.

Use **AI Usage Widget: Refresh Copilot Usage** for a manual refresh or **AI Usage Widget: Disconnect Copilot Account** to stop sharing. Server rate limits are respected even for manual refreshes. Closing VS Code leaves the last reading visible; the widget marks it stale after three minutes.

After installing an updated companion, run **Developer: Reload Window** in VS Code if it asks for a reload, then run **AI Usage Widget: Connect Copilot Account**. This command uses the current widget pairing, so retries do not invalidate an approval already in progress. Choose **Allow** in VS Code's GitHub access prompt. A cancelled prompt, declined consent, unavailable authentication provider, changed pairing, and local storage failure each have a specific message. **Show details** opens **AI Usage Widget — Copilot** in the Output panel. The log and local diagnostic file contain only the companion version, a fixed connection step/error code, and time; they never contain the original exception, stack, account, token, or API response.

This extension runs locally, including when editing through WSL, SSH, or a dev container. It does not read source code or send workspace information. GitHub authentication stays inside VS Code. Only whitelisted quota/credit/reset values, your selected account label, and GitHub host are written to the widget's per-user data folder. Raw responses, tokens, email lists, and organization memberships are never written there. Company extension/OAuth policies may require approval or prevent access.

The entitlement endpoint used by VS Code is an internal GitHub API and can change. Paid-account compatibility requires validation against the account's actual VS Code usage dashboard; automated fixtures do not establish that validation. VS Code 1.120 or later is required. No server or VS Code Marketplace account is needed for VSIX installation.

Source references: [VS Code authentication API](https://code.visualstudio.com/api/references/vscode-api#authentication), [quota interpretation](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/services/chat/common/chatEntitlementService.ts), [Copilot Business billing](https://docs.github.com/en/copilot/concepts/billing-and-usage/organizations-and-enterprises/billing).
