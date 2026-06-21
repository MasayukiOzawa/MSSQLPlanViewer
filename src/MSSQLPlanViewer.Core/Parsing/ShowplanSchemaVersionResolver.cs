using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Parsing;

internal static class ShowplanSchemaVersionResolver
{
    private static readonly IReadOnlyDictionary<string, ShowplanSchemaVersion> NamespaceMap =
        new Dictionary<string, ShowplanSchemaVersion>(StringComparer.OrdinalIgnoreCase)
        {
            ["http://schemas.microsoft.com/sqlserver/2004/07/showplan"] = ShowplanSchemaVersion.SqlServer2004,
            ["http://schemas.microsoft.com/sqlserver/2012/01/showplan"] = ShowplanSchemaVersion.SqlServer2012,
            ["http://schemas.microsoft.com/sqlserver/2014/07/showplan"] = ShowplanSchemaVersion.SqlServer2014,
            ["http://schemas.microsoft.com/sqlserver/2017/03/showplan"] = ShowplanSchemaVersion.SqlServer2017,
            ["http://schemas.microsoft.com/sqlserver/2022/ShowPlan"] = ShowplanSchemaVersion.SqlServer2022
        };

    public static ShowplanSchemaVersion Resolve(string namespaceUri) =>
        NamespaceMap.TryGetValue(namespaceUri, out var schemaVersion)
            ? schemaVersion
            : ShowplanSchemaVersion.Unknown;
}
