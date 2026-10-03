using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UsageWidget;

// Deterministic generation from the vector master; no image editors or
// platform-specific resizing tools are needed to regenerate release assets.
internal static class IconAssets
{
    internal static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    internal static void GenerateCodex(string assets)
    {
        using var image = Raster(new CodexArtwork(), 512, 1);
        image.Save(Path.Combine(assets, "codex.png"));
        Console.WriteLine("Generated Codex terminal mark PNG from the provider vector.");
    }

    internal static void Generate(string assets)
    {
        Directory.CreateDirectory(Path.Combine(assets, "AppIcon.iconset"));
        byte[] Render(int size, bool tray = false)
        {
            using var image = Raster(new Artwork(tray), size, 1);
            using var stream = new MemoryStream(); image.Save(stream); return stream.ToArray();
        }
        File.WriteAllBytes(Path.Combine(assets, "widget.png"), Render(1024));
        File.WriteAllBytes(Path.Combine(assets, "tray.png"), Render(44, true));
        foreach (var size in new[] { 16, 32, 128, 256, 512 })
        {
            File.WriteAllBytes(Path.Combine(assets, $"AppIcon.iconset/icon_{size}x{size}.png"), Render(size));
            File.WriteAllBytes(Path.Combine(assets, $"AppIcon.iconset/icon_{size}x{size}@2x.png"), Render(size * 2));
        }
        foreach (var tray in new[] { false, true })
        {
            var images = IconSizes.Select(size => Render(size, tray)).ToArray();
            using var writer = new BinaryWriter(File.Create(Path.Combine(assets, tray ? "tray.ico" : "widget.ico")));
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Length);
            var offset = 6 + 16 * images.Length;
            for (var i = 0; i < images.Length; i++)
            {
                writer.Write((byte)(IconSizes[i] == 256 ? 0 : IconSizes[i]));
                writer.Write((byte)(IconSizes[i] == 256 ? 0 : IconSizes[i]));
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
        Console.WriteLine("Generated 1024px app icon, native macOS iconset, nine-size Windows ICOs, and 44px menu-bar icon.");
    }

    internal static RenderTargetBitmap Raster(Control control, double size, double scale)
    {
        control.Measure(new(size, size)); control.Arrange(new(0, 0, size, size));
        var image = new RenderTargetBitmap(new PixelSize((int)Math.Round(size * scale), (int)Math.Round(size * scale)), new(96 * scale, 96 * scale));
        image.Render(control); return image;
    }

    private sealed class CodexArtwork : Control
    {
        private readonly VectorIcon icon = new("codex");
        public override void Render(DrawingContext context) => icon.Draw(context, new Rect(Bounds.Size), Brushes.Black);
    }

    private sealed class Artwork(bool tray) : Control
    {
        private readonly VectorIcon master = new("widget");
        public override void Render(DrawingContext context)
        {
            var size = Bounds.Width;
            if (!tray && size >= 64) { master.Draw(context, new Rect(Bounds.Size)); return; }
            // Optical small-size variant: whole-pixel bar edges and wider
            // counters make the mark legible at 16/20px without sharpening halos.
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#F7F9FC")), null, new Rect(0, 0, size, size));
            var width = Math.Max(3, Math.Round(size * .1875));
            var bottom = Math.Round(size * .75);
            foreach (var (x, top, color) in new[] { (.25, .3125, "#3675C4"), (.5625, .1875, "#8074AA") })
                context.DrawRectangle(new SolidColorBrush(Color.Parse(color)), null,
                    new Rect(Math.Round(size * x), Math.Round(size * top), width, bottom - Math.Round(size * top)), width / 2, width / 2);
        }
    }
}
