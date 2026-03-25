namespace TomestonePhone.Shared.Models;

public sealed record ReportRecord(
    Guid Id,
    ReportCategory Category,
    Guid ReporterAccountId,
    string ReporterDisplayName,
    Guid? TargetAccountId,
    Guid? TargetConversationId,
    Guid? TargetMessageId,
    Guid? TargetImageId,
    string Reason,
    bool SuspectedCsam,
    ReportStatus Status,
    DateTimeOffset CreatedAtUtc);
