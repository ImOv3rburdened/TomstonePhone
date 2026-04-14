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
        state.EnsureLoadStarted(() => this.Load(path, state));
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

    private void Load(string path, IconState state)
    {
        try
        {
            var bytes = this.LoadBytes(path);
            if (bytes is null || bytes.Length == 0)
            {
                state.SetFailed();
                return;
            }

            var wrap = this.textureProvider.CreateFromImageAsync(bytes).GetAwaiter().GetResult();
            state.SetTexture(wrap);
        }
        catch
        {
            state.SetFailed();
        }
    }

    private byte[]? LoadBytes(string path)
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
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        return File.ReadAllBytes(path);
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

        public void EnsureLoadStarted(Action load)
        {
            if (Interlocked.Exchange(ref this.loadStarted, 1) == 0)
            {
                load();
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

