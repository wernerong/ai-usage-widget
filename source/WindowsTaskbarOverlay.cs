using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UsageWidget;

// The taskbar can move above other topmost windows when the foreground changes.
// Repair only taskbar occlusion, without activating the widget or moving it.
internal sealed class WindowsTaskbarOverlay : IDisposable
{
    private readonly Window window;
    private readonly Func<bool> enabled;
    private readonly WinEvent callback;
    private readonly nint foregroundHook, reorderHook;
    private bool disposed, pending;
    private readonly DispatcherTimer fallback = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public WindowsTaskbarOverlay(Window window, Func<bool> enabled)
    {
        this.window = window;
        this.enabled = enabled;
        callback = OnEvent;
        foregroundHook = SetWinEventHook(3, 3, 0, callback, 0, 0, 2);
        reorderHook = SetWinEventHook(0x8004, 0x8004, 0, callback, 0, 0, 2);
        // Some shell restacks emit no WinEvent. Only repair actual taskbar overlap.
        fallback.Tick += (_, _) => EnsureAboveTaskbar();
        fallback.Start();
    }
    private void OnEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || pending) return;
        pending = true;
        Dispatcher.UIThread.Post(() => { pending = false; EnsureAboveTaskbar(); });
    }
    public void EnsureAboveTaskbar()
    {
        if (disposed || !enabled() || !window.IsVisible || !window.Topmost) return;
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "HWND" || !GetWindowRect(handle.Handle, out var widget)) return;
        var name = new StringBuilder(128);
        // Inspect windows above ours, including taskbars on secondary displays.
        var above = GetWindow(handle.Handle, 3); // GW_HWNDPREV
        for (var i = 0; above != 0 && i < 512; i++, above = GetWindow(above, 3))
        {
            if (!IsWindowVisible(above)) continue;
            GetClassName(above, name, name.Capacity);
            if (name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) continue;
            if (!GetWindowRect(above, out var bar) || widget.Left >= bar.Right || widget.Right <= bar.Left || widget.Top >= bar.Bottom || widget.Bottom <= bar.Top) continue;
            // HWND_TOPMOST; NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER.
            SetWindowPos(handle.Handle, -1, 0, 0, 0, 0, 0x0013 | 0x0200);
            break;
        }
    }
    internal static PixelRect? TaskbarOn(PixelRect screen)
    {
        foreach (var className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint bar = 0;
            while ((bar = FindWindowEx(0, bar, className, null)) != 0)
            {
                if (!GetWindowRect(bar, out var r)) continue;
                var bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (bounds.Width > bounds.Height && bounds.Height >= 16 && bounds.Intersects(screen)) return bounds;
            }
        }
        return null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        fallback.Stop();
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        if (reorderHook != 0) UnhookWinEvent(reorderHook);
        GC.KeepAlive(callback);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate void WinEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
