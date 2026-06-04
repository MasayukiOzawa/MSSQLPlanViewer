namespace MSSQLPlanViewer.Core.Rendering;

public interface IPlanGraphSvgRenderer
{
    string Render(StatementGraphLayout layout, GraphRenderOptions? options = null);
}
