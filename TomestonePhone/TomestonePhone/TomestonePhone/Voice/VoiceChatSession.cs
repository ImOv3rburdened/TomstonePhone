using System.Collections.Concurrent;
using System.Net.WebSockets;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Voice;

public sealed class VoiceChatSession : IDisposable
{
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<Guid, RemoteSpeakerState> remoteSpeakers = new();
    private readonly byte[] captureAccumulator = new byte[64 * 1024];
    private readonly byte[] receiveBuffer = new byte[8 * 1024];
    private int captureAccumulatorLength;
    private ClientWebSocket? webSocket;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? receiveLoopTask;
    private WaveInEvent? waveIn;
    private WaveOutEvent? waveOut;
    private MixingSampleProvider? mixer;
    private VolumeSampleProvider? playbackVolumeProvider;
    private WaveFormat? waveFormat;
    private Guid sessionId;
    private Guid currentAccountId;
    private int frameBytes;
    private int frameMilliseconds = 20;
    private bool disposed;
    private bool isMuted;
    private bool reduceBackgroundNoise;
    private float highPassAlpha = 1f;
    private float previousCaptureSample;
    private float previousFilteredSample;
    private float estimatedNoiseFloor = 120f;
    private bool voiceGateOpen;
    private int voiceGateHoldFrames;
    private float micVolume = 1f;
    private float outputVolume = 1f;

    public bool IsConnected { get; private set; }

    public Guid SessionId => this.sessionId;

    public async Task StartAsync(
        string serverBaseUrl,
        string authToken,
        Guid accountId,
        ActiveCallState call,
        int inputDeviceNumber = -1,
        int outputDeviceNumber = -1,
        bool reduceBackgroundNoise = false,
        float micVolume = 1f,
        float outputVolume = 1f,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(authToken);

        if (call.VoiceSession is null)
        {
            throw new InvalidOperationException("This call does not have a voice session.");
        }

        await this.StopAsync().ConfigureAwait(false);

        var voiceSession = call.VoiceSession;
        var sampleRate = Math.Max(8000, voiceSession.SampleRateHz);
        const byte sampleBits = 16;
        const byte channels = 1;
        try
        {
            this.frameMilliseconds = Math.Clamp(voiceSession.FrameSizeMs <= 0 ? 20 : voiceSession.FrameSizeMs, 10, 60);
            this.frameBytes = sampleRate * channels * (sampleBits / 8) * this.frameMilliseconds / 1000;
            this.waveFormat = new WaveFormat(sampleRate, sampleBits, channels);
            this.sessionId = call.SessionId;
            this.currentAccountId = accountId;
            this.isMuted = call.IsMuted;
            this.reduceBackgroundNoise = reduceBackgroundNoise;
            this.micVolume = ClampVolume(micVolume);
            this.outputVolume = ClampVolume(outputVolume);
            this.ResetNoiseReductionState(sampleRate);

            this.mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels))
            {
                ReadFully = true,
            };
            this.playbackVolumeProvider = new VolumeSampleProvider(this.mixer)
            {
                Volume = this.outputVolume,
            };

            this.waveOut = new WaveOutEvent
            {
                DeviceNumber = outputDeviceNumber,
                DesiredLatency = this.frameMilliseconds * 2,
                NumberOfBuffers = 3,
            };
            this.waveOut.Init(this.playbackVolumeProvider.ToWaveProvider16());
            this.waveOut.Play();

            this.waveIn = new WaveInEvent
            {
                DeviceNumber = inputDeviceNumber,
                BufferMilliseconds = this.frameMilliseconds,
                NumberOfBuffers = 3,
                WaveFormat = this.waveFormat,
            };
            this.waveIn.DataAvailable += this.OnCaptureDataAvailable;
            this.waveIn.RecordingStopped += this.OnRecordingStopped;

            this.webSocket = new ClientWebSocket();
            this.webSocket.Options.SetRequestHeader("Authorization", $"Bearer {authToken}");
            var uri = BuildVoiceWebSocketUri(serverBaseUrl, call.SessionId);
            this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await this.webSocket.ConnectAsync(uri, this.cancellationTokenSource.Token).ConfigureAwait(false);

            this.receiveLoopTask = Task.Run(() => this.ReceiveLoopAsync(this.cancellationTokenSource.Token), this.cancellationTokenSource.Token);
            this.waveIn.StartRecording();
            this.IsConnected = true;
        }
        catch
        {
            await this.StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void SetMuted(bool muted)
    {
        this.isMuted = muted;
    }

    public void SetLevels(float micVolume, float outputVolume)
    {
        this.micVolume = ClampVolume(micVolume);
        this.outputVolume = ClampVolume(outputVolume);

        lock (this.syncRoot)
        {
            if (this.playbackVolumeProvider is not null)
            {
                this.playbackVolumeProvider.Volume = this.outputVolume;
            }
        }
    }

    public async Task StopAsync()
    {
        this.IsConnected = false;
        this.cancellationTokenSource?.Cancel();

        if (this.waveIn is not null)
        {
            this.waveIn.DataAvailable -= this.OnCaptureDataAvailable;
            this.waveIn.RecordingStopped -= this.OnRecordingStopped;
            try
            {
                this.waveIn.StopRecording();
            }
            catch
            {
            }

            this.waveIn.Dispose();
            this.waveIn = null;
        }

        if (this.webSocket is not null)
        {
            try
            {
                if (this.webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call ended", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            this.webSocket.Dispose();
            this.webSocket = null;
        }

        if (this.receiveLoopTask is not null)
        {
            try
            {
                await this.receiveLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            this.receiveLoopTask = null;
        }

        this.cancellationTokenSource?.Dispose();
        this.cancellationTokenSource = null;
        this.captureAccumulatorLength = 0;

        foreach (var speaker in this.remoteSpeakers.Values)
        {
            speaker.Dispose();
        }

        this.remoteSpeakers.Clear();

        if (this.waveOut is not null)
        {
            try
            {
                this.waveOut.Stop();
            }
            catch
            {
            }

            this.waveOut.Dispose();
            this.waveOut = null;
        }

        this.mixer = null;
        this.playbackVolumeProvider = null;
        this.waveFormat = null;
        this.sessionId = Guid.Empty;
        this.currentAccountId = Guid.Empty;
        this.frameMilliseconds = 20;
        this.reduceBackgroundNoise = false;
        this.micVolume = 1f;
        this.outputVolume = 1f;
        this.ResetNoiseReductionState(16000);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.StopAsync().GetAwaiter().GetResult();
    }

    private static Uri BuildVoiceWebSocketUri(string serverBaseUrl, Guid sessionId)
    {
        var baseUri = new Uri(serverBaseUrl, UriKind.Absolute);
        var builder = new UriBuilder(baseUri)
        {
            Scheme = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = $"/ws/calls/{sessionId}",
            Query = string.Empty,
        };

        return builder.Uri;
    }

    private async Task SendFrameAsync(byte[] frame)
    {
        var socket = this.webSocket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return;
        }

        await this.sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.disposed || this.webSocket is null || this.webSocket.State != WebSocketState.Open)
            {
                return;
            }

            await this.webSocket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            this.sendLock.Release();
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (this.disposed || this.webSocket is null || this.webSocket.State != WebSocketState.Open || this.isMuted)
        {
            return;
        }

        lock (this.syncRoot)
        {
            if (args.BytesRecorded <= 0)
            {
                return;
            }

            if (this.captureAccumulatorLength + args.BytesRecorded > this.captureAccumulator.Length)
            {
                this.captureAccumulatorLength = 0;
            }

            Buffer.BlockCopy(args.Buffer, 0, this.captureAccumulator, this.captureAccumulatorLength, args.BytesRecorded);
            this.captureAccumulatorLength += args.BytesRecorded;

            while (this.captureAccumulatorLength >= this.frameBytes)
            {
                var frame = new byte[this.frameBytes];
                Buffer.BlockCopy(this.captureAccumulator, 0, frame, 0, this.frameBytes);
                Buffer.BlockCopy(this.captureAccumulator, this.frameBytes, this.captureAccumulator, 0, this.captureAccumulatorLength - this.frameBytes);
                this.captureAccumulatorLength -= this.frameBytes;
                var processedFrame = this.ProcessCapturedFrame(frame);
                if (processedFrame is not null)
                {
                    _ = this.SendFrameAsync(processedFrame);
                }
            }
        }
    }

    private byte[]? ProcessCapturedFrame(byte[] frame)
    {
        if (!this.reduceBackgroundNoise)
        {
            return frame;
        }

        const int bytesPerSample = 2;
        if (frame.Length < bytesPerSample)
        {
            return frame;
        }

        var sumSquares = 0f;
        var peak = 0f;
        for (var offset = 0; offset < frame.Length; offset += bytesPerSample)
        {
            var sample = (short)(frame[offset] | (frame[offset + 1] << 8));
            var amplifiedSample = sample * this.micVolume;
            var filtered = this.highPassAlpha * (this.previousFilteredSample + amplifiedSample - this.previousCaptureSample);
            this.previousCaptureSample = amplifiedSample;
            this.previousFilteredSample = filtered;

            var clamped = (short)Math.Clamp((int)MathF.Round(filtered), short.MinValue, short.MaxValue);
            frame[offset] = (byte)(clamped & 0xFF);
            frame[offset + 1] = (byte)((clamped >> 8) & 0xFF);

            var magnitude = MathF.Abs(filtered);
            peak = MathF.Max(peak, magnitude);
            sumSquares += filtered * filtered;
        }

        var sampleCount = frame.Length / bytesPerSample;
        var rms = sampleCount <= 0 ? 0f : MathF.Sqrt(sumSquares / sampleCount);
        return this.ShouldSuppressFrame(rms, peak) ? null : frame;
    }

    private bool ShouldSuppressFrame(float rms, float peak)
    {
        const float minimumNoiseFloor = 80f;
        if (this.estimatedNoiseFloor <= 0f)
        {
            this.estimatedNoiseFloor = MathF.Max(rms, minimumNoiseFloor);
        }

        var nearNoiseFloor = rms <= this.estimatedNoiseFloor * 1.35f;
        if (!this.voiceGateOpen || nearNoiseFloor)
        {
            var targetNoiseFloor = Math.Clamp(rms, minimumNoiseFloor, 2000f);
            this.estimatedNoiseFloor = (this.estimatedNoiseFloor * 0.92f) + (targetNoiseFloor * 0.08f);
        }

        var openThreshold = MathF.Max(this.estimatedNoiseFloor * 3.0f, 260f);
        var closeThreshold = MathF.Max(this.estimatedNoiseFloor * 1.8f, 170f);
        var openPeakThreshold = MathF.Max(openThreshold * 2.2f, 900f);
        var closePeakThreshold = MathF.Max(closeThreshold * 2.0f, 700f);

        var shouldOpen = rms >= openThreshold || peak >= openPeakThreshold;
        if (shouldOpen)
        {
            this.voiceGateOpen = true;
            this.voiceGateHoldFrames = 6;
            return false;
        }

        if (this.voiceGateOpen)
        {
            var shouldStayOpen = rms >= closeThreshold || peak >= closePeakThreshold;
            if (shouldStayOpen)
            {
                this.voiceGateHoldFrames = 6;
                return false;
            }

            if (this.voiceGateHoldFrames > 0)
            {
                this.voiceGateHoldFrames--;
                return false;
            }
        }

        this.voiceGateOpen = false;
        this.voiceGateHoldFrames = 0;
        return true;
    }

    private void ResetNoiseReductionState(int sampleRate)
    {
        const float cutoffHz = 120f;
        var safeSampleRate = Math.Max(sampleRate, 8000);
        var dt = 1d / safeSampleRate;
        var rc = 1d / (2d * Math.PI * cutoffHz);
        this.highPassAlpha = (float)(rc / (rc + dt));
        this.previousCaptureSample = 0f;
        this.previousFilteredSample = 0f;
        this.estimatedNoiseFloor = 120f;
        this.voiceGateOpen = false;
        this.voiceGateHoldFrames = 0;
    }

    private static float ClampVolume(float value)
    {
        return Math.Clamp(value, 0.25f, 3f);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        this.IsConnected = false;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (this.webSocket is null || this.waveFormat is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && this.webSocket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult? result;
            do
            {
                result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(this.receiveBuffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                ms.Write(this.receiveBuffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var packet = ms.ToArray();
            if (packet.Length <= 16)
            {
                continue;
            }

            var senderIdBytes = new byte[16];
            Buffer.BlockCopy(packet, 0, senderIdBytes, 0, 16);
            var senderId = new Guid(senderIdBytes);
            if (senderId == this.currentAccountId)
            {
                continue;
            }

            var payload = new byte[packet.Length - 16];
            Buffer.BlockCopy(packet, 16, payload, 0, payload.Length);
            var speaker = this.remoteSpeakers.GetOrAdd(senderId, this.CreateRemoteSpeaker);
            if (payload.Length > 0)
            {
                speaker.Buffer.AddSamples(payload, 0, payload.Length);
            }
        }
    }

    private RemoteSpeakerState CreateRemoteSpeaker(Guid _)
    {
        if (this.waveFormat is null || this.mixer is null)
        {
            throw new InvalidOperationException("Voice playback is not initialized.");
        }

        var buffer = new BufferedWaveProvider(this.waveFormat)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };
        var sampleProvider = buffer.ToSampleProvider();
        lock (this.syncRoot)
        {
            this.mixer.AddMixerInput(sampleProvider);
        }

        return new RemoteSpeakerState(buffer, sampleProvider);
    }

    private sealed class RemoteSpeakerState : IDisposable
    {
        public RemoteSpeakerState(BufferedWaveProvider buffer, ISampleProvider sampleProvider)
        {
            this.Buffer = buffer;
            this.SampleProvider = sampleProvider;
        }

        public BufferedWaveProvider Buffer { get; }

        public ISampleProvider SampleProvider { get; }

        public void Dispose()
        {
        }
    }
}
