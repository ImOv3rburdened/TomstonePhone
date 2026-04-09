using TomestonePhone.Shared.Models;

namespace TomestonePhone;

public sealed class ActiveCallState
{
    public required Guid SessionId { get; init; }

    public required Guid CallId { get; init; }

    public required Guid ConversationId { get; init; }

    public required string Title { get; set; }

    public required List<string> Participants { get; init; }

    public VoiceSessionInfo? VoiceSession { get; set; }

    public bool IsMuted { get; set; }

    public bool IsIncoming { get; set; }

    public bool IsGroup { get; set; }

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
}
