namespace TomestonePhone.Shared.Models;

public sealed record ReportReplyRequest(Guid ReportId, string ReplyBody, bool OpenStaffChat);
