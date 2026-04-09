using System.Text.Json;
using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public sealed class JsonPhoneRepository : IPhoneRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string statePath;
    private readonly string backupRoot;
    private readonly BootstrapOwnerOptions bootstrapOwner;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private bool initialized;

    public JsonPhoneRepository(IHostEnvironment environment, Microsoft.Extensions.Options.IOptions<BootstrapOwnerOptions> bootstrapOwner)
    {
        var dataRoot = Path.Combine(environment.ContentRootPath, "AppData");
        Directory.CreateDirectory(dataRoot);
        this.statePath = Path.Combine(dataRoot, "state.json");
        this.backupRoot = Path.Combine(dataRoot, "MigrationBackups");
        Directory.CreateDirectory(this.backupRoot);
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

        if (!File.Exists(this.statePath))
        {
            var state = SeedData.Create(this.bootstrapOwner);
            await this.SaveStateAsync(state, cancellationToken);
        }
    }

    private async Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        var payload = await this.LoadStatePayloadAsync(cancellationToken);
        var state = this.DeserializeState(payload);
        while (AppStateMigrator.TryMigrateNext(state, out var fromVersion, out var toVersion))
        {
            var backupPath = await this.CreateMigrationBackupAsync(fromVersion, toVersion, payload, cancellationToken);
            try
            {
                var migratedPayload = JsonSerializer.Serialize(state, this.jsonOptions);
                await this.SaveStatePayloadAsync(migratedPayload, cancellationToken);
                payload = migratedPayload;
                await this.MarkMigrationBackupCompletedAsync(backupPath, fromVersion, toVersion, null, cancellationToken);
            }
            catch (Exception ex)
            {
                await this.TryRestoreStatePayloadAsync(payload, cancellationToken);
                await this.MarkMigrationBackupCompletedAsync(backupPath, fromVersion, toVersion, ex.Message, cancellationToken);
                throw new InvalidOperationException($"Migration {fromVersion} -> {toVersion} failed. Restore attempted from backup.", ex);
            }
        }
    }

    private async Task<PersistedAppState> LoadStateAsync(CancellationToken cancellationToken)
    {
        var payload = await this.LoadStatePayloadAsync(cancellationToken);
        return this.DeserializeState(payload);
    }

    private async Task<string> LoadStatePayloadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this.statePath))
        {
            var state = SeedData.Create(this.bootstrapOwner);
            var payload = JsonSerializer.Serialize(state, this.jsonOptions);
            await this.SaveStatePayloadAsync(payload, cancellationToken);
            return payload;
        }

        return await File.ReadAllTextAsync(this.statePath, cancellationToken);
    }

    private PersistedAppState DeserializeState(string payload)
    {
        return JsonSerializer.Deserialize<PersistedAppState>(payload, this.jsonOptions) ?? SeedData.Create(this.bootstrapOwner);
    }

    private async Task SaveStateAsync(PersistedAppState state, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(state, this.jsonOptions);
        await this.SaveStatePayloadAsync(payload, cancellationToken);
    }

    private async Task SaveStatePayloadAsync(string payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(this.statePath, payload, cancellationToken);
    }

    private async Task<string> CreateMigrationBackupAsync(int fromVersion, int toVersion, string payload, CancellationToken cancellationToken)
    {
        var fileName = $"state.v{fromVersion:D3}-to-v{toVersion:D3}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
        var path = Path.Combine(this.backupRoot, fileName);
        await File.WriteAllTextAsync(path, payload, cancellationToken);
        return path;
    }

    private Task MarkMigrationBackupCompletedAsync(string backupPath, int fromVersion, int toVersion, string? failureMessage, CancellationToken cancellationToken)
    {
        var markerPath = backupPath + ".status.txt";
        var status = failureMessage is null
            ? $"Applied migration {fromVersion} -> {toVersion} at {DateTime.UtcNow:O}"
            : $"Failed migration {fromVersion} -> {toVersion} at {DateTime.UtcNow:O}{Environment.NewLine}{failureMessage}";
        return File.WriteAllTextAsync(markerPath, status, cancellationToken);
    }

    private async Task TryRestoreStatePayloadAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            await this.SaveStatePayloadAsync(payload, cancellationToken);
        }
        catch
        {
            // Leave the original failure as the primary error and rely on the backup file for manual retry/recovery.
        }
    }
}

