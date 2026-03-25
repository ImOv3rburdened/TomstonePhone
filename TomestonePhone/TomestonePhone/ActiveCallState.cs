namespace TomestonePhone;

public sealed class ActiveCallState
{
    public required Guid CallId { get; init; }

    public required string Title { get; set; }

    public required List<string> Participants { get; init; }

    public bool IsMuted { get; set; }

    public bool IsIncoming { get; set; }

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
}
