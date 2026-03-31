namespace TomestonePhone.Server.Models;

public sealed class PersistedServerAnnouncement
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByAccountId { get; set; }

    public string CreatedByDisplayName { get; set; } = string.Empty;
}