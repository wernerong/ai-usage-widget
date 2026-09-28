using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace UsageWidget;

// The checked-in artwork deliberately uses only filled paths. Render geometry
// directly at the destination DPI: never shrink a large PNG into a tiny logo.
internal sealed class VectorIcon
{
    private readonly Rect viewBox;
    private readonly (Geometry Geometry, IBrush Brush)[] paths;

    internal VectorIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{name}.svg"));
        var root = XDocument.Load(stream).Root!;
        var box = root.Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        viewBox = new Rect(box[0], box[1], box[2], box[3]);
        paths = root.Elements().Where(e => e.Name.LocalName == "path").Select(e =>
        {
            var rule = (e.Attribute("fill-rule") ?? root.Attribute("fill-rule"))?.Value == "evenodd" ? "F0 " : "F1 ";
            var geometry = StreamGeometry.Parse(rule + e.Attribute("d")!.Value);
            var fill = e.Attribute("fill")?.Value ?? "#000000";
            return ((Geometry)geometry, (IBrush)new SolidColorBrush(Color.Parse(fill)));
        }).ToArray();
        if (paths.Length == 0) throw new InvalidDataException($"No paths in icon {name}.");
    }

    internal void Draw(DrawingContext context, Rect target, IBrush? tint = null)
    {
        var scale = Math.Min(target.Width / viewBox.Width, target.Height / viewBox.Height);
        var x = target.X + (target.Width - viewBox.Width * scale) / 2;
        var y = target.Y + (target.Height - viewBox.Height * scale) / 2;
        using var transform = context.PushTransform(Matrix.CreateTranslation(-viewBox.X, -viewBox.Y)
            * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y));
        foreach (var path in paths) context.DrawGeometry(tint ?? path.Brush, null, path.Geometry);
    }

    internal static Rect Align(Rect target, double scaling) => new(
        Math.Round(target.X * scaling) / scaling, Math.Round(target.Y * scaling) / scaling,
        Math.Round(target.Width * scaling) / scaling, Math.Round(target.Height * scaling) / scaling);
}
