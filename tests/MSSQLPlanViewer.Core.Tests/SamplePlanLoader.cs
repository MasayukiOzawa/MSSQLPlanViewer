namespace MSSQLPlanViewer.Core.Tests;

internal static class SamplePlanLoader
{
    public static string Load(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName));
}
