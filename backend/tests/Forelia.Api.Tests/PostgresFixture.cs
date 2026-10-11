using Forelia.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Forelia.Api.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string databaseName = "forelia_test_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("FORELIA_TEST_POSTGRES")
        ?? "Host=localhost;Port=5433;Database=postgres;Username=forelia;Password=forelia_local";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";
    private bool created;

    public async Task InitializeAsync()
    {
        await ExecuteAdminAsync($"CREATE DATABASE {databaseName}");
        created = true;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName }.ToString();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Forelia", ConnectionString);
        });
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ForeliaDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (created) await ExecuteAdminAsync($"DROP DATABASE {databaseName} WITH (FORCE)");
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
