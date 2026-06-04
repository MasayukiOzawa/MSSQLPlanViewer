using SkiaSharp;
using Svg.Skia;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphPngExporter : IPlanGraphPngExporter
{
    public byte[] Export(string svg, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        using var svgDocument = new SKSvg();
        var picture = svgDocument.FromSvg(svg) ?? svgDocument.Picture;
        if (picture is null)
        {
            throw new InvalidOperationException("Unable to render the graph as PNG.");
        }

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
