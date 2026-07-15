using System.Collections.Concurrent;
using System.Numerics;
using System.Web;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace TomestonePhone.UI;

public sealed class GifEmbedRenderer : IDisposable
{
    private const int MaxCachedGifs = 24;
    private const int MaxGifBytes = 12 * 1024 * 1024;
    private const int MaxDimension = 2048;
    private const int MaxFrameCount = 120;
    private const long MaxDecodedBytes = 128L * 1024 * 1024;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
    private readonly ITextureProvider textureProvider;
    private readonly ConcurrentDictionary<string, GifAnimationState> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource loadCancellation = new();

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

        return IsSafeRemoteUri(uri) && uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    public void Draw(string url, float maxWidth, bool animate)
    {
        url = url.Replace("&amp;", "&");
        var state = this.cache.GetOrAdd(url, static _ => new GifAnimationState());
        state.Touch();
        this.TrimCache(url);
        state.EnsureLoadStarted(() => this.LoadAsync(url, state, this.loadCancellation.Token));

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
                if (!string.IsNullOrWhiteSpace(state.Error))
                {
                    ImGui.TextWrapped(state.Error);
                }
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
        this.loadCancellation.Cancel();
        foreach (var state in this.cache.Values)
        {
            state.Dispose();
        }

        this.cache.Clear();
        this.loadCancellation.Dispose();
    }

    private async Task LoadAsync(string url, GifAnimationState state, CancellationToken cancellationToken)
    {
        try
        {
            url = System.Net.WebUtility.HtmlDecode(url);
            url = url.Replace(".com//media", ".com/media");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var requestedUri) || !IsSafeRemoteUri(requestedUri))
            {
                state.SetFailed("Only public HTTPS GIF URLs are supported.");
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestedUri);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            );
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                state.SetFailed($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}\nURL: {url}");
                return;
            }

            if (response.RequestMessage?.RequestUri is not { } finalUri || !IsSafeRemoteUri(finalUri))
            {
                state.SetFailed("The GIF redirected to an unsupported address.");
                return;
            }

            if (response.Content.Headers.ContentLength is > MaxGifBytes)
            {
                state.SetFailed("GIF is too large.");
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffered = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
            var info = await Image.IdentifyAsync(buffered, cancellationToken).ConfigureAwait(false);
            if (info is null || info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension)
            {
                state.SetFailed("GIF dimensions are unsupported.");
                return;
            }

            buffered.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(new DecoderOptions { MaxFrames = MaxFrameCount }, buffered, cancellationToken).ConfigureAwait(false);
            var decodedBytes = (long)image.Width * image.Height * image.Frames.Count * 4;
            if (image.Frames.Count > MaxFrameCount || decodedBytes > MaxDecodedBytes)
            {
                state.SetFailed("GIF animation is too complex.");
                return;
            }
            var frames = new List<GifFrameData>(image.Frames.Count);

            for (var index = 0; index < image.Frames.Count; index++)
            {
                using var frameImage = image.Frames.CloneFrame(index);

                var pixels = new byte[frameImage.Width * frameImage.Height * 4];
                frameImage.CopyPixelDataTo(pixels);

                var metadata = image.Frames[index].Metadata.GetGifMetadata();
                var delay = Math.Max(0.06f, metadata.FrameDelay / 100f);

                frames.Add(new GifFrameData(
                    pixels,
                    frameImage.Width,
                    frameImage.Height,
                    delay
                ));
            }

            state.SetDecodedFrames(frames);
        }
        catch (Exception ex)
        {
            state.SetFailed("LoadAsync: " + ex.Message);
        }
    }

    private static bool IsSafeRemoteUri(Uri uri)
    {
        var normalizedHost = uri.Host.TrimEnd('.');
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.HostNameType == UriHostNameType.Dns
            && !string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            && !normalizedHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && !normalizedHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }

            if (destination.Length + read > MaxGifBytes)
            {
                destination.Dispose();
                throw new InvalidDataException("GIF is too large.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private void TrimCache(string currentUrl)
    {
        if (this.cache.Count <= MaxCachedGifs)
        {
            return;
        }

        foreach (var entry in this.cache
                     .Where(item => !string.Equals(item.Key, currentUrl, StringComparison.OrdinalIgnoreCase) && item.Value.CanEvict)
                     .OrderBy(item => item.Value.LastAccessUtc)
                     .Take(this.cache.Count - MaxCachedGifs))
        {
            if (this.cache.TryRemove(entry.Key, out var removed))
            {
                removed.Dispose();
            }
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
        private int disposed;
        private List<GifFrameData>? decodedFrames;

        public GifLoadStatus Status { get; private set; } = GifLoadStatus.Loading;

        public string? Error { get; private set; }

        public List<GifFrameTexture> Frames { get; } = [];

        public DateTime LastAccessUtc { get; private set; } = DateTime.UtcNow;

        public bool CanEvict => this.Status is GifLoadStatus.Ready or GifLoadStatus.Failed;

        public void Touch()
        {
            this.LastAccessUtc = DateTime.UtcNow;
        }

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
            if (Volatile.Read(ref this.disposed) != 0)
            {
                return;
            }

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
                    var wrap = textureProvider.CreateFromRaw(
                        RawImageSpecification.Rgba32(frame.Width, frame.Height),
                        frame.RgbaBytes
                    );

                    this.Frames.Add(new GifFrameTexture(wrap, frame.DelaySeconds));
            }

            this.decodedFrames = null;

            if (this.Frames.Count <= 0)
            {
                this.SetFailed();
                return;
            }

            this.currentFrameIndex = 0;
            this.nextFrameUtc = DateTime.UtcNow.AddSeconds(this.Frames[0].DelaySeconds);
            this.Status = GifLoadStatus.Ready;
            }
            catch (Exception ex)
            {
                this.SetFailed("TryCreateTextures :" + ex.Message);
            }
        }

        public void SetFrames(List<GifFrameTexture> frames)
        {
            this.Frames.AddRange(frames);
            this.currentFrameIndex = 0;
            this.nextFrameUtc = DateTime.UtcNow.AddSeconds(this.Frames[0].DelaySeconds);
            this.Status = GifLoadStatus.Ready;
        }

        public void SetFailed(string? error = null)
        {
            if (Volatile.Read(ref this.disposed) != 0)
            {
                return;
            }

            this.Error = error;
            this.Status = GifLoadStatus.Failed;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.decodedFrames = null;
            foreach (var frame in this.Frames)
            {
                frame.Dispose();
            }

            this.Frames.Clear();
        }
    }

    private sealed record GifFrameData(byte[] RgbaBytes, int Width, int Height, float DelaySeconds);

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
