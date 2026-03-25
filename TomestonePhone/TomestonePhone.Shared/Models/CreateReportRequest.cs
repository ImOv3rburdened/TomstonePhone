namespace TomestonePhone.Shared.Models;

public sealed record CreateReportRequest(
    ReportCategory Category,
    Guid? TargetAccountId,
    Guid? TargetConversationId,
    Guid? TargetMessageId,
    Guid? TargetImageId,
    string Reason,
    bool SuspectedCsam);
