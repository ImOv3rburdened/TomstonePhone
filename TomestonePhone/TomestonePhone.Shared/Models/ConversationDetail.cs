namespace TomestonePhone.Shared.Models;

public sealed record ConversationDetail(
    Guid Id,
    string Name,
    bool IsGroup,
    bool IsReadOnly,
    Guid? LinkedSupportTicketId,
    IReadOnlyList<ConversationMemberRecord> Members,
    IReadOnlyList<ExternalMediaEmbedRecord> Embeds);
