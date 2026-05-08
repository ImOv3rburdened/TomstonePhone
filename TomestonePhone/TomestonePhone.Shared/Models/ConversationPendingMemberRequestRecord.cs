namespace TomestonePhone.Shared.Models;

public sealed record ConversationPendingMemberRequestRecord(
    Guid TargetAccountId,
    string TargetDisplayName,
    string TargetPhoneNumber,
    Guid RequestedByAccountId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAtUtc);
