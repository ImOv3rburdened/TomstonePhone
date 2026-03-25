using System.Text.Json;
using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public sealed class JsonPhoneRepository : IPhoneRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string statePath;
    private readonly BootstrapOwnerOptions bootstrapOwner;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public JsonPhoneRepository(IHostEnvironment environment, Microsoft.Extensions.Options.IOptions<BootstrapOwnerOptions> bootstrapOwner)
    {
        var dataRoot = Path.Combine(environment.ContentRootPath, "AppData");
        Directory.CreateDirectory(dataRoot);
        this.statePath = Path.Combine(dataRoot, "state.json");
        this.bootstrapOwner = bootstrapOwner.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            _ = await this.LoadStateAsync(cancellationToken);
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

    private async Task<PersistedAppState> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this.statePath))
        {
            var state = SeedData.Create(this.bootstrapOwner);
            await this.SaveStateAsync(state, cancellationToken);
            return state;
        }

        await using var stream = File.OpenRead(this.statePath);
        var stateFromDisk = await JsonSerializer.DeserializeAsync<PersistedAppState>(stream, this.jsonOptions, cancellationToken);
        return stateFromDisk ?? SeedData.Create(this.bootstrapOwner);
    }

    private async Task SaveStateAsync(PersistedAppState state, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(this.statePath);
        await JsonSerializer.SerializeAsync(stream, state, this.jsonOptions, cancellationToken);
    }
}
