using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DealerOS.IntegrationTests;

public sealed class ReconditioningPostgresFixture : IAsyncLifetime
{
    private const string DatabaseName = "dealeros_reconditioning_tests";
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername("dealeros")
        .WithPassword("dealeros")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "dealeros"))
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task PrepareDatabaseAsync()
    {
        await EnsureStartedAsync();
        await ResetDatabaseAsync();
    }

    private async Task EnsureStartedAsync()
    {
        if (ConnectionString.Length > 0) return;

        await _startGate.WaitAsync();
        try
        {
            if (ConnectionString.Length > 0) return;

            await _postgres.StartAsync();
            ConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
            {
                Pooling = false,
                SslMode = SslMode.Disable
            }.ConnectionString;
        }
        finally
        {
            _startGate.Release();
        }
    }

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
        await _postgres.DisposeAsync();
        _startGate.Dispose();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReconditioningPostgresCollection : ICollectionFixture<ReconditioningPostgresFixture>
{
    public const string Name = "Reconditioning PostgreSQL";
}
