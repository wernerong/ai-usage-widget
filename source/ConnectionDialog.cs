using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UsageWidget;

internal sealed class ConnectionDialog : Window
{
    internal static bool Supports(string id) => CodingPlanProviders.Ids.Contains(id) || id is "copilot" or "antigravity" or "claude" or "cursor" or "gemini";
    internal static string Instructions(string id) => id switch
    {
        "claude" => "Sign in to Claude Code using /login with your Claude subscription. The widget reads the existing login on this computer. Keep Claude Code signed in to renew expired tokens. API billing keys are not supported.",
        "cursor" => "Install Cursor CLI and run agent login. The widget reads its subscription login on this computer, including the selected team. Editor-only and API-key logins are not currently supported.",
        "gemini" => "Run Gemini CLI and choose Sign in with Google. The widget reads its cached Google login and Code Assist quotas. Open Gemini CLI to renew expired tokens. Gemini web chat, API keys, and Vertex billing are separate allowances.",
        "antigravity" => "Connect the documented Antigravity CLI status line, then restart agy and run /usage. Only quota values are saved. The widget shows the last reported quota and marks it stale after 3 minutes. Existing custom status lines are preserved; see the README to chain them.",
        "copilot" => "The widget can reuse a local Copilot hosts/apps login or GitHub CLI (gh auth login). Optionally enter a GitHub token authorized for Copilot below. A general API key without Copilot access may be rejected.",
        _ => "Enter your coding-plan subscription API key. Select the provider for the region where you subscribed: CN is mainland China; the unmarked provider is international. Keys are saved in Windows Credential Manager or macOS Keychain, never widget settings."
    };
    internal ConnectionDialog(ProviderDefinition provider)
    {
        Title = "Connect " + provider.Name; Width = 450; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        stack.Children.Add(new TextBlock { Text = provider.Name, FontSize = 23, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = Instructions(provider.Id), TextWrapping = TextWrapping.Wrap });
        var credential = new TextBox { PasswordChar = '●', Watermark = "Subscription key / authorized token" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var keyBased = CodingPlanProviders.Ids.Contains(provider.Id) || provider.Id == "copilot";
        if (keyBased) stack.Children.Add(credential);
        stack.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => { credential.Text = ""; Close(false); };
        if (keyBased)
        {
            var forget = new Button { Content = "Forget saved key" };
            forget.Click += async (_, _) =>
            {
                try { await Task.Run(() => CredentialVault.Save(provider.Id, null)); credential.Text = ""; Close(true); }
                catch { status.Text = "Could not remove the connection. Unlock your credential store and try again."; }
            };
            buttons.Children.Add(forget);
        }
        var save = new Button { Content = provider.Id == "antigravity" ? "Connect status line" : keyBased ? "Save and connect" : "Connect existing login" };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (keyBased && !string.IsNullOrWhiteSpace(credential.Text))
                {
                    var secret = ProviderApi.Require(credential.Text.Trim(), "Enter a valid key.");
                    await Task.Run(() => CredentialVault.Save(provider.Id, secret));
                }
                else if (keyBased && provider.Id != "copilot") { status.Text = "Enter your subscription key first."; return; }
                if (provider.Id == "antigravity") AntigravityProvider.Connect();
                credential.Text = ""; Close(true);
            }
            catch (InvalidOperationException ex) { status.Text = ex.Message; }
            catch { status.Text = "Could not connect. Unlock your credential store and check the provider setup."; }
            finally { save.IsEnabled = true; }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); stack.Children.Add(buttons); Content = stack;
    }
}
