using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace TomestonePhone.UI;

public sealed class GifEmbedRenderer : IDisposable
{
    private static readonly HttpClient HttpClient = new();
    private readonly ITextureProvider textureProvider;
    private readonly ConcurrentDictionary<string, GifAnimationState> cache = new(StringComparer.OrdinalIgnoreCase);

    public GifEmbedRenderer(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
    }

    public bool IsGifUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    public void Draw(string url, float maxWidth, bool animate)
    {
        var state = this.cache.GetOrAdd(url, static _ => new GifAnimationState());
        state.EnsureLoadStarted(() => this.LoadAsync(url, state));

        switch (state.Status)
        {
            case GifLoadStatus.Loading:
                ImGui.TextDisabled("Loading GIF...");
                return;
            case GifLoadStatus.Decoded:
                state.TryCreateTextures(this.textureProvider);
                if (state.Status != GifLoadStatus.Ready)
                {
                    ImGui.TextDisabled(state.Status == GifLoadStatus.Failed ? "GIF unavailable" : "Loading GIF...");
                    return;
                }

                var decodedFrame = state.GetCurrentFrame(animate);
                var decodedSize = this.GetScaledSize(decodedFrame.Wrap.Width, decodedFrame.Wrap.Height, maxWidth);
                ImGui.Image(decodedFrame.Wrap.Handle, decodedSize);
                return;
            case GifLoadStatus.Failed:
                ImGui.TextDisabled("GIF unavailable");
                return;
            case GifLoadStatus.Ready when state.Frames.Count > 0:
                var frame = state.GetCurrentFrame(animate);
                var size = this.GetScaledSize(frame.Wrap.Width, frame.Wrap.Height, maxWidth);
                ImGui.Image(frame.Wrap.Handle, size);
                return;
            default:
                ImGui.TextDisabled("GIF unavailable");
                return;
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

    private async Task LoadAsync(string url, GifAnimationState state)
    {
        try
        {
            using var stream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false);
            using var image = await Image.LoadAsync<Rgba32>(stream).ConfigureAwait(false);
            var frames = new List<GifFrameData>(image.Frames.Count);

            for (var index = 0; index < image.Frames.Count; index++)
            {
                using var frameImage = image.Frames.CloneFrame(index);
                using var output = new MemoryStream();
                await frameImage.SaveAsPngAsync(output).ConfigureAwait(false);
                var metadata = frameImage.Frames.RootFrame.Metadata.GetGifMetadata();
                var delay = Math.Max(0.06f, metadata.FrameDelay / 100f);
                frames.Add(new GifFrameData(output.ToArray(), delay));
            }

            state.SetDecodedFrames(frames);
        }
        catch
        {
            state.SetFailed();
        }
    }

    private Vector2 GetScaledSize(int width, int height, float maxWidth)
    {
        if (width <= 0 || height <= 0)
        {
            return new Vector2(MathF.Min(maxWidth, 220f), 124f);
        }

        var scale = MathF.Min(1f, maxWidth / width);
        return new Vector2(width * scale, height * scale);
    }

    private enum GifLoadStatus
    {
        Loading,
        Decoded,
        Ready,
        Failed,
    }

    private sealed class GifAnimationState : IDisposable
    {
        private int currentFrameIndex;
        private DateTime nextFrameUtc = DateTime.UtcNow;
        private int loadStarted;
        private List<GifFrameData>? decodedFrames;

        public GifLoadStatus Status { get; private set; } = GifLoadStatus.Loading;

        public List<GifFrameTexture> Frames { get; } = [];

        public void EnsureLoadStarted(Func<Task> load)
        {
            if (Interlocked.Exchange(ref this.loadStarted, 1) == 0)
            {
                _ = Task.Run(load);
            }
        }

        public GifFrameTexture GetCurrentFrame(bool animate)
        {
            if (this.Frames.Count == 0)
            {
                throw new InvalidOperationException("No GIF frames are loaded.");
            }

            if (!animate || this.Frames.Count == 1)
            {
                return this.Frames[this.currentFrameIndex];
            }

            var now = DateTime.UtcNow;
            if (now >= this.nextFrameUtc)
            {
                this.currentFrameIndex = (this.currentFrameIndex + 1) % this.Frames.Count;
                this.nextFrameUtc = now.AddSeconds(this.Frames[this.currentFrameIndex].DelaySeconds);
            }

            return this.Frames[this.currentFrameIndex];
        }

        public void SetDecodedFrames(List<GifFrameData> frames)
        {
            this.decodedFrames = frames;
            this.Status = GifLoadStatus.Decoded;
        }

        public void TryCreateTextures(ITextureProvider textureProvider)
        {
            if (this.Status != GifLoadStatus.Decoded || this.decodedFrames is null)
            {
                return;
            }

            try
            {
                foreach (var frame in this.decodedFrames)
                {
                    var wrap = textureProvider.CreateFromImageAsync(frame.PngBytes).GetAwaiter().GetResult();
                    this.Frames.Add(new GifFrameTexture(wrap, frame.DelaySeconds));
                }

                this.decodedFrames = null;
                this.currentFrameIndex = 0;
                this.nextFrameUtc = DateTime.UtcNow.AddSeconds(this.Frames[0].DelaySeconds);
                this.Status = GifLoadStatus.Ready;
            }
            catch
            {
                this.SetFailed();
            }
        }

        public void SetFrames(List<GifFrameTexture> frames)
        {
            this.Frames.AddRange(frames);
            this.currentFrameIndex = 0;
            this.nextFrameUtc = DateTime.UtcNow.AddSeconds(this.Frames[0].DelaySeconds);
            this.Status = GifLoadStatus.Ready;
        }

        public void SetFailed()
        {
            this.Status = GifLoadStatus.Failed;
        }

        public void Dispose()
        {
            this.decodedFrames = null;
            foreach (var frame in this.Frames)
            {
                frame.Dispose();
            }

            this.Frames.Clear();
        }
    }

    private sealed record GifFrameData(byte[] PngBytes, float DelaySeconds);

    private sealed class GifFrameTexture : IDisposable
    {
        public GifFrameTexture(IDalamudTextureWrap wrap, float delaySeconds)
        {
            this.Wrap = wrap;
            this.DelaySeconds = delaySeconds;
        }

        public IDalamudTextureWrap Wrap { get; }

        public float DelaySeconds { get; }

        public void Dispose()
        {
            this.Wrap.Dispose();
        }
    }
}



