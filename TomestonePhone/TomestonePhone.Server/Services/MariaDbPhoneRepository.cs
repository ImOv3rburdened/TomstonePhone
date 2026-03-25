using System.Text.Json;
using MySqlConnector;
using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public sealed class MariaDbPhoneRepository : IPhoneRepository
{
    private const string StateTableName = "app_state";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly BootstrapOwnerOptions bootstrapOwner;
    private readonly string connectionString;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private bool initialized;

    public MariaDbPhoneRepository(Microsoft.Extensions.Options.IOptions<MariaDbOptions> mariaDb, Microsoft.Extensions.Options.IOptions<BootstrapOwnerOptions> bootstrapOwner)
    {
        var options = mariaDb.Value;
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Server,
            Port = (uint)Math.Max(0, options.Port),
            Database = options.Database,
            UserID = options.Username,
            Password = options.Password,
            SslMode = Enum.TryParse<MySqlSslMode>(options.SslMode, true, out var sslMode) ? sslMode : MySqlSslMode.None,
            AllowUserVariables = true,
        };

        this.connectionString = builder.ConnectionString;
        this.bootstrapOwner = bootstrapOwner.Value;
    }

    public async Task<T> ReadAsync<T>(Func<PersistedAppState, T> action, CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            await this.EnsureInitializedAsync(cancellationToken);
            var state = await this.LoadStateAsync(cancellationToken);
            return action(state);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<PersistedAppState, T> action, CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            await this.EnsureInitializedAsync(cancellationToken);
            var state = await this.LoadStateAsync(cancellationToken);
            var result = action(state);
            await this.SaveStateAsync(state, cancellationToken);
            return result;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (this.initialized)
        {
            return;
        }

        await using var connection = new MySqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);

        var createTableSql =
            $"""
            CREATE TABLE IF NOT EXISTS {StateTableName} (
                id TINYINT NOT NULL PRIMARY KEY,
                state_json LONGTEXT NOT NULL,
                updated_at_utc DATETIME(6) NOT NULL
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            """;

        await using (var command = new MySqlCommand(createTableSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var rowExistsSql = $"SELECT COUNT(*) FROM {StateTableName} WHERE id = 1;";
        await using (var command = new MySqlCommand(rowExistsSql, connection))
        {
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                await this.SaveStateAsync(SeedData.Create(this.bootstrapOwner), connection, cancellationToken);
            }
        }

        this.initialized = true;
    }

    private async Task<PersistedAppState> LoadStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT state_json FROM {StateTableName} WHERE id = 1 LIMIT 1;";
        await using var command = new MySqlCommand(sql, connection);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            var seeded = SeedData.Create(this.bootstrapOwner);
            await this.SaveStateAsync(seeded, connection, cancellationToken);
            return seeded;
        }

        return JsonSerializer.Deserialize<PersistedAppState>(payload, this.jsonOptions) ?? SeedData.Create(this.bootstrapOwner);
    }

    private async Task SaveStateAsync(PersistedAppState state, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await this.SaveStateAsync(state, connection, cancellationToken);
    }

    private async Task SaveStateAsync(PersistedAppState state, MySqlConnection connection, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(state, this.jsonOptions);
        var sql =
            $"""
            INSERT INTO {StateTableName} (id, state_json, updated_at_utc)
            VALUES (1, @payload, @updatedAtUtc)
            ON DUPLICATE KEY UPDATE
                state_json = VALUES(state_json),
                updated_at_utc = VALUES(updated_at_utc);
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
