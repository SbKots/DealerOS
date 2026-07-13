using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DealerOS.IntegrationTests;

public sealed class ReconditioningPostgresFixture : IAsyncLifetime
{
    private const string DatabaseName = "dealeros_reconditioning_tests";
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _testGate = new(1, 1);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername("dealeros")
        .WithPassword("dealeros")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "dealeros"))
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<IAsyncDisposable> BeginTestAsync()
    {
        await _testGate.WaitAsync();
        try
        {
            await EnsureStartedAsync();
            await ResetDatabaseAsync();
            return new TestLease(_testGate);
        }
        catch
        {
            _testGate.Release();
            throw;
        }
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
        try
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
        catch (Exception exception)
        {
            throw await CreateContainerFailureAsync(exception);
        }
    }

    private async Task<InvalidOperationException> CreateContainerFailureAsync(Exception exception)
    {
        var diagnostics = new List<string>
        {
            $"State={_postgres.State}",
            $"Id={_postgres.Id}"
        };

        try
        {
            diagnostics.Add($"ExitCode={await _postgres.GetExitCodeAsync(CancellationToken.None)}");
        }
        catch (Exception diagnosticException)
        {
            diagnostics.Add($"ExitCode=unavailable ({diagnosticException.Message})");
        }

        try
        {
            var (stdout, stderr) = await _postgres.GetLogsAsync();
            diagnostics.Add($"stdout={stdout}");
            diagnostics.Add($"stderr={stderr}");
        }
        catch (Exception diagnosticException)
        {
            diagnostics.Add($"logs=unavailable ({diagnosticException.Message})");
        }

        return new InvalidOperationException(
            $"Reconditioning PostgreSQL reset failed. {string.Join(Environment.NewLine, diagnostics)}",
            exception);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        _testGate.Dispose();
        _startGate.Dispose();
    }

    private sealed class TestLease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReconditioningPostgresCollection : ICollectionFixture<ReconditioningPostgresFixture>
{
    public const string Name = "Reconditioning PostgreSQL";
}
