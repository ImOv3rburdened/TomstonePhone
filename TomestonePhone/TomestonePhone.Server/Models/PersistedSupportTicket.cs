namespace TomestonePhone.Server.Models;

public sealed class PersistedSupportTicket
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string Status { get; set; } = "Open";

    public bool IsModerationCase { get; set; }

    public string QuarantinedImagePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
