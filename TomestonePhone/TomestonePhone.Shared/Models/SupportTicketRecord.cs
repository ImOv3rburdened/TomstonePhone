namespace TomestonePhone.Shared.Models;

public sealed record SupportTicketRecord(
    Guid Id,
    string Subject,
    string Body,
    SupportTicketStatus Status,
    DateTimeOffset CreatedAtUtc,
    bool IsModerationCase);
