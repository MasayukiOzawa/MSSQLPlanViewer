namespace MSSQLPlanViewer.Core.Rendering;

public interface IPlanGraphPngExporter
{
    byte[] Export(string svg, int width, int height);
}
