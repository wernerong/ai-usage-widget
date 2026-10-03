using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using UsageWidget;

internal static class IconChecks
{
    internal static void Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        using (var stream = AssetLoader.Open(new Uri("avares://AIUsageWidget/Assets/widget.png")))
        using (var image = new Bitmap(stream)) Check(image.PixelSize == new PixelSize(1024, 1024), "Application master is 1024px");
        foreach (var name in new[] { "widget", "tray" })
        {
            using var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{name}.ico"));
            using var reader = new BinaryReader(stream);
            Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Valid ICO header");
            var frames = reader.ReadUInt16();
            Check(frames == IconAssets.IconSizes.Length, "ICO contains all DPI-specific frames");
            foreach (var size in IconAssets.IconSizes)
            {
                int width = reader.ReadByte(), height = reader.ReadByte();
                Check((width == 0 ? 256 : width) == size && width == height, "ICO frame dimensions");
                reader.ReadUInt16(); reader.ReadUInt16();
                Check(reader.ReadUInt16() == 32, "ICO has full alpha depth");
                var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
                Check(length > 0 && offset + length <= stream.Length, "ICO frame points to complete image");
                var position = stream.Position;
                stream.Position = offset;
                using (var png = new MemoryStream(reader.ReadBytes((int)length)))
                using (var image = new Bitmap(png)) Check(image.PixelSize == new PixelSize(size, size), "ICO frame PNG matches its directory");
                stream.Position = position;
            }
        }
        string[] providers = ["codex", "grok", "claude", "cursor", "githubcopilot", "gemini", "antigravity", "kimi", "zai", "minimax"];
        using (var stream = AssetLoader.Open(new Uri("avares://AIUsageWidget/Assets/codex.svg")))
        {
            var root = System.Xml.Linq.XDocument.Load(stream).Root!;
            Check(root.Elements().Single(e => e.Name.LocalName == "title").Value == "Codex", "Codex uses dedicated artwork, not the OpenAI asset");
        }
        using (var codex = IconAssets.Raster(new Logo("codex"), 120, 1))
        {
            var buffer = Marshal.AllocHGlobal(120 * 120 * 4);
            try
            {
                codex.CopyPixels(new PixelRect(codex.PixelSize), buffer, 120 * 120 * 4, 120 * 4);
                byte Alpha(int x, int y) => Marshal.ReadByte(buffer, (y * 120 + x) * 4 + 3);
                Check(Alpha(60, 20) > 240 && Alpha(60, 60) > 240, "Codex has a solid silhouette instead of the ChatGPT knot");
                Check(Alpha(41, 59) < 20 && Alpha(75, 77) < 20, "Codex terminal chevron and underscore remain transparent");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 3 })
        foreach (var size in new[] { 12, 16, 17 })
        foreach (var name in providers)
        {
            using var image = IconAssets.Raster(new Logo(name), size, scale);
            var length = image.PixelSize.Width * image.PixelSize.Height * 4;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                image.CopyPixels(new PixelRect(image.PixelSize), buffer, length, image.PixelSize.Width * 4);
                var pixels = new byte[length]; Marshal.Copy(buffer, pixels, 0, length);
                var covered = pixels.Where((_, i) => i % 4 == 3).Count(alpha => alpha > 32);
                var area = image.PixelSize.Width * image.PixelSize.Height;
                Check(covered > area * .05 && covered < area * .95, $"Vector {name} has visible paths and transparent counters at {size}px / {scale}x");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        foreach (var dark in new[] { false, true })
        {
            var proof = new Proof(providers, dark);
            proof.Measure(new(600, 220)); proof.Arrange(new(0, 0, 600, 220));
            using var bitmap = new RenderTargetBitmap(new(1200, 440), new(192, 192));
            bitmap.Render(proof); bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"icon-proof-{(dark ? "dark" : "light")}.png"));
        }
        Console.WriteLine($"PASS: {count} icon checks (vector scale coverage, app resolution, multi-frame ICO integrity).");
    }

    private sealed class Logo(string name) : Control
    {
        private readonly VectorIcon icon = new(name);
        public override void Render(DrawingContext context) => icon.Draw(context, new Rect(Bounds.Size), Brushes.Black);
    }

    private sealed class Proof(string[] names, bool dark) : Control
    {
        public override void Render(DrawingContext context)
        {
            var foreground = dark ? Brushes.White : Brushes.Black;
            context.FillRectangle(dark ? new SolidColorBrush(Color.Parse("#202228")) : Brushes.White, new Rect(Bounds.Size));
            for (var i = 0; i < names.Length; i++)
            {
                var icon = new VectorIcon(names[i]);
                for (var row = 0; row < 4; row++)
                {
                    var size = new[] { 12, 16, 24, 32 }[row];
                    icon.Draw(context, new Rect(14 + i * 59, 12 + row * 48, size, size), foreground);
                }
            }
        }
    }
}
