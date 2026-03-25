namespace TomestonePhone.Shared.Models;

public sealed record ReportReplyResult(Guid ReportId, Guid? ConversationId, string Status);
