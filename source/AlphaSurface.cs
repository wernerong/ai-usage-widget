using System.Runtime.InteropServices;

namespace UsageWidget;

// Per-pixel alpha preserves antialiasing at the circle boundary. A window Region
// only supports binary clipping and cannot represent these translucent edge pixels.
internal static class AlphaSurface
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] struct Size { public int Width, Height; public Size(int w, int h) { Width=w; Height=h; } }
    [StructLayout(LayoutKind.Sequential, Pack=1)] struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destDC, ref Point dest, ref Size size, IntPtr srcDC, ref Point src, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongW")] static extern int SetStyle(IntPtr hwnd, int index, int style);
    public static void Enable(IntPtr hwnd)
    {
        var style = GetStyle(hwnd, -20);
        SetStyle(hwnd, -20, style & ~0x80000);
        SetStyle(hwnd, -20, style | 0x80000);
    }
    public static void Disable(IntPtr hwnd) => SetStyle(hwnd, -20, GetStyle(hwnd, -20) & ~0x80000);
    internal static bool IsLayered(IntPtr hwnd) => (GetStyle(hwnd, -20) & 0x80000) != 0;
    public static void Present(Form form, Bitmap bitmap, double opacity)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var image = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memory, image);
        try
        {
            var dest = new Point(form.Left, form.Top); var src = new Point(0, 0);
            var size = new Size(bitmap.Width, bitmap.Height);
            var blend = new Blend { Alpha = (byte)Math.Round(255 * Math.Clamp(opacity, 0.5, 1)), Format = 1 };
            if (!UpdateLayeredWindow(form.Handle, screen, ref dest, ref size, memory, ref src, 0, ref blend, 2))
            {
                // WinForms can apply constant opacity again during Show(), after
                // OnHandleCreated. Windows requires resetting this style before
                // switching from SetLayeredWindowAttributes to per-pixel alpha.
                Enable(form.Handle);
                if (!UpdateLayeredWindow(form.Handle, screen, ref dest, ref size, memory, ref src, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally { SelectObject(memory, old); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
}
