namespace TomestonePhone.Shared.Models;

public sealed record ConversationSummary(
    Guid Id,
    string DisplayName,
    bool IsGroup,
    string LastMessagePreview,
    DateTimeOffset LastActivityUtc,
    int UnreadCount);
