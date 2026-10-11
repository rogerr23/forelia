using Forelia.Api.Data;
using Forelia.Api.Dashboard;
using Forelia.Api.Health;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ImportContract.MaxRequestBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ImportContract.MaxRequestBytes);
var connectionString = builder.Configuration.GetConnectionString("Forelia")
    ?? throw new InvalidOperationException("Configure a conexão Forelia.");
builder.Services.AddDbContext<ForeliaDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.ProblemDetails.Status == 500)
    {
        context.ProblemDetails.Title = "Não foi possível concluir a solicitação.";
        context.ProblemDetails.Detail = "Ocorreu uma falha técnica. Tente novamente mais tarde.";
    }
});
builder.Services.AddSingleton<CsvValidator>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ReconciliationService>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/", () => Results.Ok(new { application = "Forelia", version = "v1" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapImportEndpoints();
app.MapReconciliationEndpoints();
app.MapAnalysisEndpoints();
app.MapDashboardEndpoints();
app.Run();

public partial class Program;
