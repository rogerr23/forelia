using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Forelia.Api.Tests;

public sealed class DevelopmentToolsTests
{
    [Fact]
    public async Task DevelopmentOffersSwaggerUploadBodiesFiltersAndDashboardPreview()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Forelia", "Host=localhost;Database=not_used;Username=not_used");
        });
        using var client = factory.CreateClient();
        var swagger = await client.GetStringAsync("/swagger/index.html");
        Assert.Contains("swagger-ui-bundle.js", swagger);
        var configuration = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains("/openapi/v1.json", configuration);
        using var script = await client.GetAsync("/swagger/swagger-ui-bundle.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        var upload = paths.GetProperty("/api/importacoes/{fonte}").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        Assert.Equal("binary", upload.GetProperty("properties").GetProperty("arquivo").GetProperty("format").GetString());
        Assert.Contains("arquivo", upload.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var create = paths.GetProperty("/api/conciliacoes").GetProperty("post");
        Assert.True(create.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        var export = paths.GetProperty("/api/conciliacoes/{id}/exportacao").GetProperty("get");
        Assert.Equal(new[] { "id", "idOperacao", "resultado", "situacaoAnalise", "dataOperacao", "dataInicial", "dataFinal" }.Order(),
            export.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("name").GetString()!).Order());
        Assert.True(paths.TryGetProperty("/api/dashboard", out _));
        var indicatorProperties = document.GetProperty("components").GetProperty("schemas")
            .GetProperty("DashboardIndicators").GetProperty("properties");
        Assert.Equal("integer", indicatorProperties.GetProperty("totalItens").GetProperty("type").GetString());
        Assert.Equal("number", indicatorProperties.GetProperty("taxaConciliacao").GetProperty("type").GetString());
        Assert.False(paths.TryGetProperty("/dashboard", out _));
        var preview = await client.GetStringAsync("/dashboard");
        Assert.Contains("Ocorrências de divergência", preview);
        Assert.Contains("/api/dashboard", preview);
        Assert.Contains("textContent", preview);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task DevelopmentPagesAreNotExposedOutsideDevelopment(string environment)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Forelia", "Host=localhost;Database=not_used;Username=not_used");
        });
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/swagger/index.html", "/openapi/v1.json", "/dashboard" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
