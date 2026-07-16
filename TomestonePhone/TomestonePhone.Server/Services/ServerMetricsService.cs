namespace TomestonePhone.Server.Services;

public sealed class ServerMetricsService
{
    private long totalBytesTransferred;
    private int activeVoiceConnections;

    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public long TotalBytesTransferred => Interlocked.Read(ref this.totalBytesTransferred);

    public int ActiveVoiceConnections => Math.Max(0, Volatile.Read(ref this.activeVoiceConnections));

    public void RecordBytes(long count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref this.totalBytesTransferred, count);
        }
    }

    public void VoiceConnected() => Interlocked.Increment(ref this.activeVoiceConnections);

    public void VoiceDisconnected() => Interlocked.Decrement(ref this.activeVoiceConnections);
}

public sealed class ThroughputCountingStream : Stream
{
    private readonly Stream inner;
    private readonly ServerMetricsService metrics;

    public ThroughputCountingStream(Stream inner, ServerMetricsService metrics)
    {
        this.inner = inner;
        this.metrics = metrics;
    }

    public override bool CanRead => this.inner.CanRead;
    public override bool CanSeek => this.inner.CanSeek;
    public override bool CanWrite => this.inner.CanWrite;
    public override long Length => this.inner.Length;
    public override long Position { get => this.inner.Position; set => this.inner.Position = value; }
    public override void Flush() => this.inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => this.inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);
    public override void SetLength(long value) => this.inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        this.inner.Write(buffer, offset, count);
        this.metrics.RecordBytes(count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await this.inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        this.metrics.RecordBytes(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await this.inner.WriteAsync(buffer, cancellationToken);
        this.metrics.RecordBytes(buffer.Length);
    }

    protected override void Dispose(bool disposing) { }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
