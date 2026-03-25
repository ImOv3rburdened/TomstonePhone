namespace TomestonePhone.Server.Models;

public sealed class PersistedCall
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsGroup { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public int DurationSeconds { get; set; }

    public bool Missed { get; set; }
}
