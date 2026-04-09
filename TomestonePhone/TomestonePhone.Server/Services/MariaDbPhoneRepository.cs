using System.Text.Json;
using MySqlConnector;
using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public sealed class MariaDbPhoneRepository : IPhoneRepository
{
    private const string StateTableName = "app_state";
    private const string BackupTableName = "app_state_migration_backups";
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
            AllowPublicKeyRetrieval = options.AllowPublicKeyRetrieval,
            AllowUserVariables = true,
        };

        this.connectionString = builder.ConnectionString;
        this.bootstrapOwner = bootstrapOwner.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            await this.EnsureInitializedAsync(cancellationToken);
            await this.ApplyPendingMigrationsAsync(cancellationToken);
            this.initialized = true;
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<T> ReadAsync<T>(Func<PersistedAppState, T> action, CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            await this.EnsureInitializedAsync(cancellationToken);
            await this.ApplyPendingMigrationsAsync(cancellationToken);
            this.initialized = true;
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
            await this.ApplyPendingMigrationsAsync(cancellationToken);
            this.initialized = true;
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

        var createStateTableSql =
            $"""
            CREATE TABLE IF NOT EXISTS {StateTableName} (
                id TINYINT NOT NULL PRIMARY KEY,
                state_json LONGTEXT NOT NULL,
                updated_at_utc DATETIME(6) NOT NULL
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            """;

        await using (var command = new MySqlCommand(createStateTableSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var createBackupTableSql =
            $"""
            CREATE TABLE IF NOT EXISTS {BackupTableName} (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                state_id TINYINT NOT NULL,
                schema_version INT NOT NULL,
                target_schema_version INT NOT NULL,
                backup_json LONGTEXT NOT NULL,
                status VARCHAR(32) NOT NULL,
                failure_message LONGTEXT NULL,
                created_at_utc DATETIME(6) NOT NULL,
                completed_at_utc DATETIME(6) NULL,
                KEY IX_{BackupTableName}_state_created (state_id, created_at_utc)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            """;

        await using (var command = new MySqlCommand(createBackupTableSql, connection))
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
    }

    private async Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);

        var payload = await this.LoadStatePayloadAsync(connection, cancellationToken);
        var state = this.DeserializeState(payload);
        while (AppStateMigrator.TryMigrateNext(state, out var fromVersion, out var toVersion))
        {
            var backupId = await this.CreateMigrationBackupAsync(connection, fromVersion, toVersion, payload, cancellationToken);
            try
            {
                var migratedPayload = JsonSerializer.Serialize(state, this.jsonOptions);
                await this.SaveStatePayloadAsync(migratedPayload, connection, cancellationToken);
                await this.MarkMigrationBackupCompletedAsync(connection, backupId, "Applied", null, cancellationToken);
                payload = migratedPayload;
            }
            catch (Exception ex)
            {
                await this.TryRestoreStatePayloadAsync(payload, connection, cancellationToken);
                await this.MarkMigrationBackupCompletedAsync(connection, backupId, "Failed", ex.Message, cancellationToken);
                throw new InvalidOperationException($"Migration {fromVersion} -> {toVersion} failed. Restore attempted from backup.", ex);
            }
        }
    }

    private async Task<PersistedAppState> LoadStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        var payload = await this.LoadStatePayloadAsync(connection, cancellationToken);
        return this.DeserializeState(payload);
    }

    private async Task<string> LoadStatePayloadAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = $"SELECT state_json FROM {StateTableName} WHERE id = 1 LIMIT 1;";
        await using var command = new MySqlCommand(sql, connection);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            var seeded = SeedData.Create(this.bootstrapOwner);
            payload = JsonSerializer.Serialize(seeded, this.jsonOptions);
            await this.SaveStatePayloadAsync(payload, connection, cancellationToken);
        }

        return payload;
    }

    private PersistedAppState DeserializeState(string payload)
    {
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
        await this.SaveStatePayloadAsync(payload, connection, cancellationToken);
    }

    private async Task SaveStatePayloadAsync(string payload, MySqlConnection connection, CancellationToken cancellationToken)
    {
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

    private async Task<long> CreateMigrationBackupAsync(MySqlConnection connection, int schemaVersion, int targetSchemaVersion, string payload, CancellationToken cancellationToken)
    {
        var sql =
            $"""
            INSERT INTO {BackupTableName} (state_id, schema_version, target_schema_version, backup_json, status, created_at_utc)
            VALUES (1, @schemaVersion, @targetSchemaVersion, @payload, 'Pending', @createdAtUtc);
            SELECT LAST_INSERT_ID();
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        command.Parameters.AddWithValue("@targetSchemaVersion", targetSchemaVersion);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private async Task MarkMigrationBackupCompletedAsync(MySqlConnection connection, long backupId, string status, string? failureMessage, CancellationToken cancellationToken)
    {
        var sql =
            $"""
            UPDATE {BackupTableName}
            SET status = @status,
                failure_message = @failureMessage,
                completed_at_utc = @completedAtUtc
            WHERE id = @id;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@failureMessage", (object?)failureMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("@id", backupId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TryRestoreStatePayloadAsync(string payload, MySqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await this.SaveStatePayloadAsync(payload, connection, cancellationToken);
        }
        catch
        {
            // Leave the original failure as the primary error and rely on the stored backup row for manual retry/recovery.
        }
    }
}
