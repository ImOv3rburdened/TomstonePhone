namespace TomestonePhone.Shared.Models;

public sealed record CreateSupportTicketRequest(string Subject, string Body, bool IsModerationCase);
