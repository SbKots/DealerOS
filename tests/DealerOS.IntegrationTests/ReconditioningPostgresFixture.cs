using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DealerOS.IntegrationTests;

public sealed class ReconditioningPostgresFixture : IAsyncLifetime
{
    private const string DatabaseName = "dealeros_reconditioning_tests";
    private readonly SemaphoreSlim _testGate = new(1, 1);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername("dealeros")
        .WithPassword("dealeros")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "dealeros"))
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Pooling = false,
            SslMode = SslMode.Disable
        }.ConnectionString;
    }

    public async Task BeginTestAsync()
    {
        await _testGate.WaitAsync();
        try
        {
            await ResetDatabaseAsync();
        }
        catch
        {
            _testGate.Release();
            throw;
        }
    }

    public void CompleteTest() => _testGate.Release();

    private async Task ResetDatabaseAsync()
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using (var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection))
        {
            await drop.ExecuteNonQueryAsync();
        }
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", connection);
        await create.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        _testGate.Dispose();
        await _postgres.DisposeAsync();
    }
}
