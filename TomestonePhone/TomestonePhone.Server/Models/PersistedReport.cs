namespace TomestonePhone.Server.Models;

public sealed class PersistedReport
{
    public Guid Id { get; set; }

    public string Category { get; set; } = "Message";

    public Guid ReporterAccountId { get; set; }

    public Guid? TargetAccountId { get; set; }

    public Guid? TargetConversationId { get; set; }

    public Guid? TargetMessageId { get; set; }

    public Guid? TargetImageId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool SuspectedCsam { get; set; }

    public string Status { get; set; } = "Open";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
