using Avalonia;

namespace UsageWidget;

internal static class ScreenPlacement
{
    internal static double TaskbarHeight(double physicalHeight, double scaling) => Math.Clamp(physicalHeight / scaling - 8, 32, 48);
    internal static PixelPoint AlignTaskbar(PixelPoint point, double height, double scaling, PixelRect bar)
    {
        var pixels = (int)Math.Ceiling(height * scaling);
        var center = point.Y + pixels / 2;
        return center >= bar.Y && center <= bar.Bottom
            ? new PixelPoint(point.X, bar.Y + (bar.Height - pixels) / 2) : point;
    }
    // Project onto each real display, not the bounding box of the entire desktop:
    // a bounding box includes invisible gaps between offset monitors.
    internal static PixelPoint Constrain(PixelPoint requested, Size size, IEnumerable<(PixelRect Area, double Scaling)> displays)
    {
        var best = requested;
        var distance = double.PositiveInfinity;
        foreach (var (area, scaling) in displays)
        {
            var width = (int)Math.Ceiling(size.Width * scaling);
            var height = (int)Math.Ceiling(size.Height * scaling);
            var candidate = new PixelPoint(
                Math.Clamp(requested.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(requested.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
            var dx = (double)candidate.X - requested.X;
            var dy = (double)candidate.Y - requested.Y;
            var score = dx * dx + dy * dy;
            if (score < distance) { distance = score; best = candidate; }
        }
        return best;
    }
}
