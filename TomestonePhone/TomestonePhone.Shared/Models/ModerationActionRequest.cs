namespace TomestonePhone.Shared.Models;

public sealed record ModerationActionRequest(
    Guid AccountId,
    ModerationActionKind Action,
    string Reason,
    string? Password = null);
