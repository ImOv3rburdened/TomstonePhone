using System.Collections.Concurrent;
using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace TomestonePhone.UI;

public sealed class AppIconRenderer : IDisposable
{
    private const string EmbeddedPrefix = "embedded://";
    private readonly ITextureProvider textureProvider;
    private readonly ConcurrentDictionary<string, IconState> cache = new(StringComparer.OrdinalIgnoreCase);

    public AppIconRenderer(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
    }

    public IDalamudTextureWrap? TryGetIcon(string? path)
    {
        return this.TryGetTexture(path);
    }

    public IDalamudTextureWrap? TryGetTexture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !this.CanResolve(path))
        {
            return null;
        }

        var state = this.cache.GetOrAdd(path, static _ => new IconState());
        state.EnsureLoadStarted(() => this.LoadAsync(path, state));
        return state.Status == IconLoadStatus.Ready ? state.Texture : null;
    }

    public void Invalidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (this.cache.TryRemove(path, out var removedState))
        {
            removedState.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var state in this.cache.Values)
        {
            state.Dispose();
        }

        this.cache.Clear();
    }

    private bool CanResolve(string path)
    {
        return path.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase) || File.Exists(path);
    }

    private async Task LoadAsync(string path, IconState state)
    {
        try
        {
            var bytes = await this.LoadBytesAsync(path).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                state.SetFailed();
                return;
            }

            var wrap = await this.textureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);
            state.SetTexture(wrap);
        }
        catch
        {
            state.SetFailed();
        }
    }

    private async Task<byte[]?> LoadBytesAsync(string path)
    {
        if (path.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = path[EmbeddedPrefix.Length..];
            var resourceName = $"TomestonePhone.EmbeddedIcons.{name}";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(false);
            return memory.ToArray();
        }

        return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
    }

    private enum IconLoadStatus
    {
        Loading,
        Ready,
        Failed,
    }

    private sealed class IconState : IDisposable
    {
        private int loadStarted;

        public IconLoadStatus Status { get; private set; } = IconLoadStatus.Loading;

        public IDalamudTextureWrap? Texture { get; private set; }

        public void EnsureLoadStarted(Func<Task> load)
        {
            if (Interlocked.Exchange(ref this.loadStarted, 1) == 0)
            {
                _ = Task.Run(load);
            }
        }

        public void SetTexture(IDalamudTextureWrap texture)
        {
            this.Texture = texture;
            this.Status = IconLoadStatus.Ready;
        }

        public void SetFailed()
        {
            this.Status = IconLoadStatus.Failed;
        }

        public void Dispose()
        {
            this.Texture?.Dispose();
            this.Texture = null;
        }
    }
}
