namespace TomestonePhone.Shared.Models;

public sealed record SupportTicketRecord(
    Guid Id,
    Guid ConversationId,
    Guid OwnerAccountId,
    string OwnerDisplayName,
    string Subject,
    string Body,
    SupportTicketStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    Guid? ClosedByAccountId,
    bool IsModerationCase);
