using Microsoft.OpenApi;

namespace Forelia.Api.Development;

public static class DevelopmentTools
{
    public static void AddDevelopmentTools(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Forelia V1";
                document.Info.Description = "Importe os CSVs, copie os IDs retornados e execute a conciliação. Consulte, analise e exporte os resultados. A prévia visual está em /dashboard.";
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                // O upload usa leitura manual para preservar a validação e os limites existentes.
                if (context.Description.RelativePath == "api/importacoes/{fonte}" && context.Description.HttpMethod == "POST")
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["multipart/form-data"] = new()
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Required = new HashSet<string> { "arquivo" },
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["arquivo"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                                    }
                                }
                            }
                        }
                    };
                }
                return Task.CompletedTask;
            });
        });
    }

    public static void MapDevelopmentTools(this WebApplication app)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Forelia V1");
            options.DocumentTitle = "Forelia — testar API";
            options.EnableTryItOutByDefault();
        });
        app.MapGet("/dashboard", () => Results.Stream(
            typeof(DevelopmentTools).Assembly.GetManifestResourceStream("Forelia.Api.Development.DashboardPreview.html")!,
            "text/html; charset=utf-8")).ExcludeFromDescription();
    }
}
