using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace MSSQLPlanViewer.Web;

internal static class OpenApiDocumentationHelpers
{
    public static void DescribeJsonRequestBody(OpenApiOperation operation, params string[] fieldDescriptions)
    {
        if (operation.RequestBody is not OpenApiRequestBody requestBody)
        {
            return;
        }

        requestBody.Required = true;
        requestBody.Description = "Required request header: Content-Type: application/json.";
        if (fieldDescriptions.Length > 0)
        {
            requestBody.Description += $"{Environment.NewLine}{Environment.NewLine}Body fields:{Environment.NewLine}"
                + string.Join(Environment.NewLine, fieldDescriptions.Select(description => $"- {description}"));
        }
    }

    public static void AddRequiredSchemaProperty(IOpenApiSchema? schema, string propertyName)
    {
        if (schema is not OpenApiSchema requestSchema)
        {
            return;
        }

        requestSchema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        requestSchema.Required.Add(propertyName);
    }

    public static bool TryGetJsonRequestBody(OpenApiOperation operation, out OpenApiMediaType jsonMediaType)
    {
        jsonMediaType = null!;
        if (operation.RequestBody?.Content is null
            || !operation.RequestBody.Content.TryGetValue("application/json", out var candidate))
        {
            return false;
        }

        jsonMediaType = candidate;
        return true;
    }

    public static void DescribeSchemaProperty(
        IOpenApiSchema? schema,
        string propertyName,
        string description,
        JsonNode? example)
    {
        if (schema is not OpenApiSchema requestSchema
            || requestSchema.Properties is null
            || !requestSchema.Properties.TryGetValue(propertyName, out var propertySchema)
            || propertySchema is not OpenApiSchema property)
        {
            return;
        }

        property.Description = description;
        property.Example = example;
    }
}
