using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Microsoft.Win32;

namespace UsageWidget;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { Checks.Run(); return; }
        if (args.Contains("--diagnose"))
        {
            using var grok = new GrokProvider();
            var results = Task.WhenAll(Diagnostic("Codex", () => CodexProvider.Read(CancellationToken.None)), Diagnostic("Grok", () => grok.Read(CancellationToken.None))).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        using var mutex = new Mutex(true, "Local\\OngWernEr.AIUsageWidget", out var first);
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\OngWernEr.AIUsageWidget.Show");
        if (!first) { showRequest.Set(); return; }
        Application.Run(new Widget(args, showRequest));
    }
    private static async Task<object> Diagnostic(string provider, Func<Task<Reading>> read)
    {
        try { return await read(); }
        catch (Exception ex) { return new { Provider = provider, Error = Widget.SafeError(ex) }; }
    }
}

internal sealed class Preferences
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Pinned { get; set; } = true;
    public bool ShowUsed { get; set; }
    public bool Compact { get; set; }
    public double Opacity { get; set; } = 0.97;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageWidget");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch { /* A read-only settings directory must not stop live monitoring. */ }
    }
}

internal sealed class ProviderState(string name)
{
    public string Name { get; } = name;
    public Reading? Reading { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? Attempted { get; set; }
    public bool Stale => Error != null || Reading is { } r && DateTimeOffset.Now - r.Fetched > TimeSpan.FromMinutes(3);
}

internal sealed class Widget : Form
{
    private readonly Preferences prefs = Preferences.Load();
    private readonly GrokProvider grok = new();
    private readonly ProviderState codexState = new("Codex"), grokState = new("Grok");
    private readonly CancellationTokenSource stop = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolTip tips = new() { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 200 };
    private readonly string[] args;
    private readonly EventWaitHandle showRequest;
    private bool busy, closing, dragging;
    private Point dragStart;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
    private string tipText = "";
    private int hover = -1;
    private readonly Bitmap codexLogo = LoadLogo("codex"), grokLogo = LoadLogo("grok");
    private static readonly Color Bg = Color.FromArgb(242, 243, 246), Card = Color.FromArgb(253, 253, 254), TextMain = Color.FromArgb(30, 32, 38), Muted = Color.FromArgb(113, 118, 129), Teal = Color.FromArgb(54, 117, 196), Blue = Color.FromArgb(128, 116, 170), Amber = Color.FromArgb(175, 111, 30);
    private float ScaleFactor => DeviceDpi / 96f;

    public Widget(string[] args, EventWaitHandle showRequest)
    {
        this.args = args;
        this.showRequest = showRequest;
        Text = "AI Usage Widget";
        AccessibleName = "AI usage: Codex and Grok";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = prefs.Pinned;
        BackColor = Bg;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        if (args.Contains("--compact")) prefs.Compact = true;
        if (args.Contains("--cards")) prefs.Compact = false;
        ApplyLayout(false);
        StartPosition = FormStartPosition.Manual;
        Opacity = prefs.Compact ? 1 : Math.Clamp(prefs.Opacity, 0.5, 1);
        Icon = CreateIcon();
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = prefs.X is { } x && prefs.Y is { } y ? new Point(x, y) : new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        ClampPosition();
        SavePosition();
        BuildMenu();
        ContextMenuStrip = menu;
        tray = new NotifyIcon { Icon = Icon, Text = "AI Usage Widget — loading", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleVisible(); };
        timer.Tick += async (_, _) =>
        {
            if (showRequest.WaitOne(0)) { ClampPosition(); Show(); Activate(); }
            Invalidate();
            UpdateCompactSurface();
            if (!busy && DateTimeOffset.Now >= nextRefresh) await RefreshUsage();
        };
        Shown += async (_, _) =>
        {
            timer.Start();
            UpdateCompactSurface();
            await RefreshUsage();
            if (args.Contains("--render-check"))
            {
                var savedOpacity = prefs.Opacity;
                prefs.Opacity = 1;
                SetCompact(false);
                if (AlphaSurface.IsLayered(Handle) || ClientSize.Height != (int)(342 * ScaleFactor))
                    throw new InvalidOperationException("Cards retained the badge surface.");
                using (var cards = new Bitmap(Width, Height))
                {
                    DrawToBitmap(cards, new Rectangle(Point.Empty, Size));
                    cards.Save(Path.Combine(AppContext.BaseDirectory, "cards-after-switch.png"), ImageFormat.Png);
                }
                prefs.Opacity = savedOpacity;
                SetCompact(true);
                SetCompact(false);
                SetCompact(true);
                prefs.Opacity = 0.7; UpdateCompactSurface();
                Hide(); Show(); UpdateCompactSurface();
                prefs.Opacity = savedOpacity; UpdateCompactSurface();
                using var sample = RenderBadges();
                var partial = 0;
                for (var y = 0; y < sample.Height; y++)
                    for (var x = 0; x < sample.Width; x++)
                        if (sample.GetPixel(x, y).A is > 0 and < 255) partial++;
                if (sample.GetPixel(0, 0).A != 0 || partial < 100) throw new InvalidOperationException("Transparent edge rendering failed.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "render-checks.txt"), $"PASS: round-to-cards clears layered surface at 100% opacity; repeated layout switching at saved opacity; startup, hide/show, and {partial} antialiased alpha pixels.\n");
            }
            if (args.Contains("--render") || args.Contains("--render-check"))
            {
                using var bitmap = prefs.Compact ? RenderBadges() : new Bitmap(Width, Height);
                if (!prefs.Compact) DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "widget-preview.png"), ImageFormat.Png);
                Quit();
            }
        };
        DpiChanged += (_, _) => { ApplyLayout(false); ClampPosition(); };
        FormClosing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); } };
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Bg); g.FillEllipse(bg, 0, 0, 31, 31);
            using var a = new SolidBrush(Teal); using var b = new SolidBrush(Blue);
            g.FillRectangle(a, 8, 9, 5, 16); g.FillRectangle(b, 19, 5, 5, 20);
        }
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private void BuildMenu()
    {
        menu.Font = new Font("Segoe UI", 9);
        menu.BackColor = Card;
        menu.ForeColor = TextMain;
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(6);
        menu.Items.Add("Show / hide widget", null, (_, _) => ToggleVisible());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshUsage());
        var layout = new ToolStripMenuItem("Layout");
        var round = new ToolStripMenuItem("Round badges (compact)");
        var cards = new ToolStripMenuItem("Detailed cards");
        round.Click += (_, _) => SetCompact(true);
        cards.Click += (_, _) => SetCompact(false);
        layout.DropDownItems.AddRange([round, cards]);
        menu.Opening += (_, _) => { round.Checked = prefs.Compact; cards.Checked = !prefs.Compact; };
        menu.Items.Add(layout);
        var pin = new ToolStripMenuItem("Always on top") { Checked = prefs.Pinned, CheckOnClick = true };
        pin.CheckedChanged += (_, _) => { prefs.Pinned = TopMost = pin.Checked; prefs.Save(); };
        menu.Items.Add(pin);
        var used = new ToolStripMenuItem("Show percentage used") { Checked = prefs.ShowUsed, CheckOnClick = true };
        used.CheckedChanged += (_, _) => { prefs.ShowUsed = used.Checked; prefs.Save(); Invalidate(); };
        menu.Items.Add(used);
        var opacity = new ToolStripMenuItem("Opacity");
        foreach (var percent in new[] { 100, 97, 85, 70 })
            opacity.DropDownItems.Add($"{percent}%", null, (_, _) => { prefs.Opacity = percent / 100.0; if (prefs.Compact) UpdateCompactSurface(); else Opacity = prefs.Opacity; prefs.Save(); });
        menu.Items.Add(opacity);
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsStartupEnabled() };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.Checked) key.SetValue("AIUsageWidget", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("AIUsageWidget", false);
            }
            catch { MessageBox.Show("Could not change startup. You can still open AI Usage Widget from Start.", Text); }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Codex usage", null, (_, _) => OpenUrl("https://chatgpt.com/codex/settings/usage"));
        menu.Items.Add("Open Grok usage", null, (_, _) => OpenUrl("https://grok.com?_s=usage"));
        menu.Items.Add("Reset position", null, (_, _) =>
        {
            var area = Screen.PrimaryScreen!.WorkingArea;
            Location = new(area.Right - Width - 18, area.Bottom - Height - 18); SavePosition(); Show();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
    }
    private static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("AIUsageWidget") != null;
    }
    private void ToggleVisible() { if (Visible) Hide(); else { ClampPosition(); Show(); Activate(); } }
    private void ClampPosition()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }
    private void SavePosition() { prefs.X = Left; prefs.Y = Top; prefs.Save(); }

    private void SetCompact(bool compact)
    {
        prefs.Compact = compact;
        tips.SetToolTip(this, ""); tipText = ""; hover = -1;
        ApplyLayout(true); ClampPosition(); SavePosition(); Invalidate();
    }
    private void ApplyLayout(bool keepBottomRight)
    {
        var right = Right; var bottom = Bottom;
        ClientSize = new((int)((prefs.Compact ? 196 : 320) * ScaleFactor), (int)((prefs.Compact ? 96 : 342) * ScaleFactor));
        var previous = Region;
        Region = null;
        previous?.Dispose();
        if (keepBottomRight) Location = new(right - Width, bottom - Height);
        if (IsHandleCreated)
        {
            // WinForms tracks Opacity separately from our native layered surface.
            // Setting Opacity=1 can be a no-op, leaving the old badge bitmap active.
            // Explicitly leave per-pixel composition before restoring card painting.
            AlphaSurface.Disable(Handle);
            Opacity = 1;
            if (prefs.Compact) { AlphaSurface.Enable(Handle); UpdateCompactSurface(); }
            else Opacity = Math.Clamp(prefs.Opacity, 0.5, 1);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (prefs.Compact) AlphaSurface.Enable(Handle);
    }
    private static Bitmap LoadLogo(string name)
    {
        var assembly = typeof(Widget).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith($"Assets.{name}.png")))!;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
    private Bitmap RenderBadges()
    {
        const int samples = 3;
        using var high = new Bitmap(Width * samples, Height * samples, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(high))
        {
            g.Clear(Color.Transparent);
            g.ScaleTransform(ScaleFactor * samples, ScaleFactor * samples);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            PaintBadges(g);
        }
        var final = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(final))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(high, new Rectangle(0, 0, Width, Height));
        }
        return final;
    }
    private void UpdateCompactSurface()
    {
        if (!prefs.Compact || !IsHandleCreated || !Visible || closing) return;
        using var bitmap = RenderBadges();
        try { AlphaSurface.Present(this, bitmap, prefs.Opacity); }
        catch (System.ComponentModel.Win32Exception) when (!args.Contains("--render") && !args.Contains("--render-check"))
        {
            // Keep the monitor usable if a display driver rejects layered windows.
            prefs.Compact = false; ApplyLayout(true); ClampPosition(); SavePosition(); Invalidate();
        }
    }

    private async Task RefreshUsage()
    {
        if (busy || closing) return;
        busy = true; Invalidate();
        await Task.WhenAll(Update(codexState, () => CodexProvider.Read(stop.Token)), Update(grokState, () => grok.Read(stop.Token)));
        if (closing) return;
        busy = false;
        nextRefresh = DateTimeOffset.Now.AddMinutes(1);
        tray.Text = $"Codex {Short(codexState, 0)} / {Short(codexState, 1)} | Grok {Short(grokState, 0)}";
        AccessibleDescription = Details(codexState) + "\n" + Details(grokState);
        // A local, credential-free health snapshot makes unattended refreshes diagnosable.
        try
        {
            Directory.CreateDirectory(Preferences.Folder);
            var health = new { Updated = DateTimeOffset.Now, ProcessId = Environment.ProcessId, Layout = prefs.Compact ? "round" : "cards", WindowVisible = Visible, AlwaysOnTop = TopMost, WindowBounds = new { Left, Top, Width, Height }, Codex = codexState.Reading, CodexError = codexState.Error, Grok = grokState.Reading, GrokError = grokState.Error };
            var healthPath = Path.Combine(Preferences.Folder, "status.json");
            File.WriteAllText(healthPath + ".tmp", JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(healthPath + ".tmp", healthPath, true);
        }
        catch { }
        UpdateCompactSurface();
        Invalidate();
    }
    private async Task Update(ProviderState state, Func<Task<Reading>> read)
    {
        try { state.Reading = await read(); state.Error = null; }
        catch (OperationCanceledException) when (closing) { return; }
        catch (Exception ex) { state.Error = SafeError(ex); }
        state.Attempted = DateTimeOffset.Now;
        if (!closing) Invalidate();
    }
    internal static string SafeError(Exception ex) => ex switch
    {
        OperationCanceledException => "Refresh timed out. Retrying automatically.",
        HttpRequestException => "Cannot connect. Check your network.",
        JsonException => "Usage response changed or login file is invalid.",
        IOException => "Cannot read the CLI login. Try again after sign-in.",
        InvalidOperationException => ex.Message,
        _ => "Usage unavailable. Check the provider login."
    };
    private static string Short(ProviderState state, int index)
    {
        var q = state.Reading?.Quotas.ElementAtOrDefault(index);
        return q?.Used is { } n ? $"{100 - n:0.#}% left{(state.Stale ? " (stale)" : "")}" : "unavailable";
    }
    internal static string ResetText(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset == null) return "Reset time unavailable";
        var d = reset.Value - now;
        if (d <= TimeSpan.Zero) return "Reset due · awaiting update";
        return d.TotalDays >= 1 ? $"Resets in {(int)d.TotalDays}d {d.Hours}h" : d.TotalHours >= 1 ? $"Resets in {(int)d.TotalHours}h {d.Minutes}m" : $"Resets in {Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))}m";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(ScaleFactor, ScaleFactor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        if (prefs.Compact) { PaintBadges(g); return; }
        Round(g, new(0.5f, 0.5f, 319, 341), 20, Bg, Color.FromArgb(217, 221, 229));
        Txt(g, "Usage", 18, 11, 20, TextMain, true);
        Round(g, new(86, 17, 64, 20), 10, Color.FromArgb(228, 231, 237));
        Txt(g, prefs.ShowUsed ? "% used" : "% left", 101, 20, 10, Muted, true);
        Txt(g, busy ? "···" : "↻", 224, 10, 23, hover == 0 ? TextMain : Muted);
        Txt(g, "—", 256, 12, 19, hover == 1 ? TextMain : Muted);
        Txt(g, "⋮", 288, 10, 23, hover == 2 ? TextMain : Muted);
        Round(g, new(12, 50, 296, 160), 16, Color.FromArgb(224, 227, 233));
        Round(g, new(12, 48, 296, 160), 16, Card, Color.FromArgb(233, 235, 240));
        Header(g, codexState, 64, Teal);
        Row(g, codexState.Reading?.Quotas.ElementAtOrDefault(0) ?? new Quota("5 hours", null, null), 89, Teal, codexState.Stale);
        Row(g, codexState.Reading?.Quotas.ElementAtOrDefault(1) ?? new Quota("Weekly", null, null), 140, Teal, codexState.Stale);
        Txt(g, codexState.Reading?.Balance ?? "Connecting to Codex…", 24, 190, 10, Muted);
        var resets = codexState.Reading?.FreeResets;
        Round(g, new(205, 186, 91, 17), 8, Color.FromArgb(235, 241, 250));
        Txt(g, resets is { } count ? $"Free resets  {count}" : "Free resets  —", 214, 189, 9,
            codexState.Stale ? Amber : resets > 0 ? Teal : Muted, resets > 0);
        Round(g, new(12, 218, 296, 107), 16, Color.FromArgb(224, 227, 233));
        Round(g, new(12, 216, 296, 107), 16, Card, Color.FromArgb(233, 235, 240));
        Header(g, grokState, 232, Blue);
        Row(g, grokState.Reading?.Quotas.FirstOrDefault() ?? new Quota("Weekly", null, null), 257, Blue, grokState.Stale);
        Txt(g, grokState.Reading?.Balance ?? "Connecting to Grok…", 24, 307, 10, Muted);
        Txt(g, "Codex + Grok", 18, 329, 9, Muted);
        Txt(g, busy ? "Updating…" : "Updates every minute", 202, 329, 9, Muted);
    }
    private void PaintBadges(Graphics g)
    {
        Badge(codexState, 0, Teal);
        Badge(grokState, 100, Blue);

        void Center(string text, float x, float y, float size, Color color, bool bold = false)
        {
            using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, x + 48 - g.MeasureString(text, font).Width / 2, y);
        }
        string Percent(Quota? q) => q?.Used is { } n ? $"{(prefs.ShowUsed ? n : 100 - n):0.#}%" : "—";
        void Ring(Quota? q, float x, float inset, Color color, bool stale)
        {
            var rect = new RectangleF(x + inset, inset, 96 - inset * 2, 96 - inset * 2);
            using var track = new Pen(Color.FromArgb(221, 225, 233), 2);
            g.DrawEllipse(track, rect);
            if (q?.Used is not { } n) return;
            using var pen = new Pen(stale ? Amber : n >= 90 ? Color.FromArgb(193, 78, 78) : color, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var value = prefs.ShowUsed ? n : 100 - n;
            if (value > 0) g.DrawArc(pen, rect, -90, (float)(3.6 * value));
        }
        void Badge(ProviderState state, float x, Color accent)
        {
            // Soft perimeter shadow and a pearl surface, kept inside the existing footprint.
            for (var i = 0; i < 3; i++)
            {
                using var shadow = new SolidBrush(Color.FromArgb(9 + i * 5, 30, 37, 52));
                g.FillEllipse(shadow, x + i, i + 1, 96 - i * 2, 95 - i * 2);
            }
            using var fill = new LinearGradientBrush(new RectangleF(x + 2, 2, 92, 92), Color.FromArgb(254, 254, 255), Color.FromArgb(232, 235, 241), 90f);
            g.FillEllipse(fill, x + 2, 2, 92, 92);
            using var rim = new Pen(Color.FromArgb(220, 255, 255, 255), 0.8f);
            g.DrawEllipse(rim, x + 2.5f, 2.5f, 91, 91);
            var first = state.Reading?.Quotas.ElementAtOrDefault(0);
            var second = state.Reading?.Quotas.ElementAtOrDefault(1);
            Ring(first, x, 6, accent, state.Stale);
            if (state.Name == "Codex") Ring(second, x, 10, Color.FromArgb(153, 170, 196), state.Stale);
            DrawLogo(g, state.Name == "Codex" ? codexLogo : grokLogo, new RectangleF(x + 23, 19, 12, 12));
            Txt(g, state.Name, x + 39, 19, 10, Muted, true);
            if (state.Name == "Codex")
            {
                Center($"5h  {Percent(first)}", x, 35, 14, TextMain, true);
                Center($"Wk  {Percent(second)}", x, 53, 11, Muted);
                var free = state.Reading?.FreeResets;
                Center(state.Stale ? "STALE" : free > 0 ? $"↻ {free} free" : prefs.ShowUsed ? "used" : "left", x, 70, 9, state.Stale ? Amber : Muted);
            }
            else
            {
                Center(Percent(first), x, 34, 22, TextMain, true);
                Center(state.Stale ? "STALE" : prefs.ShowUsed ? "week used" : "week left", x, 62, 10, state.Stale ? Amber : Muted);
            }
        }
    }
    private void Header(Graphics g, ProviderState state, float y, Color accent)
    {
        DrawLogo(g, state.Name == "Codex" ? codexLogo : grokLogo, new RectangleF(24, y - 3, 17, 17));
        Txt(g, state.Name, 48, y - 3, 14, TextMain, true);
        var age = state.Reading is { } r ? DateTimeOffset.Now - r.Fetched : (TimeSpan?)null;
        var status = state.Stale ? "Stale · hover for details" : age == null ? busy ? "Connecting" : "Unavailable" : "Live";
        Txt(g, status, state.Stale ? 176 : age == null ? 237 : 273, y, 9, state.Stale ? Amber : Muted);
    }
    private void Row(Graphics g, Quota quota, float y, Color accent, bool stale)
    {
        Txt(g, quota.Label, 24, y, 11, Muted);
        var value = quota.Used is { } n ? (prefs.ShowUsed ? n : 100 - n) : (double?)null;
        var color = stale ? Amber : quota.Used >= 90 ? Color.FromArgb(193, 78, 78) : quota.Used >= 75 ? Amber : accent;
        var label = value is { } v ? $"{v:0.#}%" : "—";
        using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(value == null ? Muted : stale || quota.Used >= 75 ? color : TextMain);
        var measured = g.MeasureString(label, font);
        g.DrawString(label, font, brush, 295 - measured.Width, y - 5);
        Round(g, new(24, y + 19, 272, 4), 2, Color.FromArgb(233, 235, 240));
        if (value is > 0) Round(g, new(24, y + 19, (float)(272 * value / 100), 4), 2, color);
        Txt(g, ResetText(quota.Reset, DateTimeOffset.Now), 24, y + 27, 10, Muted);
    }
    private static void Txt(Graphics g, string s, float x, float y, float size, Color color, bool bold = false)
    {
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        g.DrawString(s, font, brush, x, y);
    }
    private static void DrawLogo(Graphics g, Bitmap logo, RectangleF rect)
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(new[] {
            new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0},
            new float[] {0,0,0,0,0}, new float[] {0,0,0,1,0},
            new float[] {0.16f,0.17f,0.2f,0,1} }));
        g.DrawImage(logo, Rectangle.Round(rect), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
    }
    private static void Round(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
    {
        if (r.Width <= 0) return;
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        using var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        using var b = new SolidBrush(fill); g.FillPath(b, p);
        if (border is { } c) { using var pen = new Pen(c); g.DrawPath(pen, p); }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && (prefs.Compact || e.Y / ScaleFactor < 44 && e.X / ScaleFactor < 215)) { dragging = true; dragStart = e.Location; Capture = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) { Location = new(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y); return; }
        var x = e.X / ScaleFactor; var y = e.Y / ScaleFactor;
        if (prefs.Compact)
        {
            Cursor = Cursors.SizeAll;
            var compactTip = Details(x < 100 ? codexState : grokState) + "\n\nDrag to move · double-click for cards · right-click for layout/settings";
            if (compactTip != tipText) { tips.SetToolTip(this, compactTip); tipText = compactTip; }
            return;
        }
        var h = y < 44 ? x >= 282 ? 2 : x >= 248 ? 1 : x >= 215 ? 0 : -1 : -1;
        if (hover != h) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        var text = h switch { 0 => "Refresh now", 1 => "Hide to tray — click the tray icon to restore", 2 => "Settings and Exit", _ => y < 44 ? "Drag to move · right-click for settings" : Details(y < 212 ? codexState : grokState) };
        if (text != tipText) { tips.SetToolTip(this, text); tipText = text; }
    }
    private string Details(ProviderState state)
    {
        var lines = new List<string> { state.Name + (state.Name == "Codex" ? " · live Codex account limits" : " · Grok CLI billing") };
        if (state.Error != null) lines.Add(state.Error);
        if (state.Reading is { } r)
        {
            lines.Add($"Last successful refresh: {r.Fetched.LocalDateTime:ddd d MMM, HH:mm:ss}");
            foreach (var q in r.Quotas)
            {
                lines.Add($"{q.Label}: {(q.Used is { } n ? $"{n:0.##}% used · {100-n:0.##}% left" : "unavailable")}");
                if (q.Reset != null) lines.Add($"Resets {q.Reset.Value.LocalDateTime:ddd d MMM, HH:mm:ss}");
                if (q.Note != null) lines.Add(q.Note);
            }
            lines.Add(r.Balance);
            if (state.Name == "Codex") lines.Add(r.FreeResets is { } count ? $"Free resets available: {count}" : "Free reset availability: unavailable");
        }
        return string.Join("\n", lines);
    }
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragging) { dragging = false; Capture = false; ClampPosition(); SavePosition(); return; }
        if (e.Button != MouseButtons.Left || e.Y / ScaleFactor >= 44) return;
        var x = e.X / ScaleFactor;
        if (x >= 282) menu.Show(this, e.Location);
        else if (x >= 248) Hide();
        else if (x >= 215) await RefreshUsage();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (prefs.Compact && e.Button == MouseButtons.Left) { dragging = false; Capture = false; SetCompact(false); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == Keys.F5) { _ = RefreshUsage(); return true; }
        if (keyData == (Keys.Alt | Keys.F4)) { Hide(); return true; }
        if (keyData == (Keys.Shift | Keys.F10)) { menu.Show(this, new Point(16, 40)); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void Quit() { closing = true; SavePosition(); timer.Stop(); stop.Cancel(); tray.Visible = false; Close(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); timer.Dispose(); tray.Dispose(); menu.Dispose(); tips.Dispose(); grok.Dispose(); stop.Dispose(); Icon?.Dispose(); codexLogo.Dispose(); grokLogo.Dispose(); }
        base.Dispose(disposing);
    }
}
