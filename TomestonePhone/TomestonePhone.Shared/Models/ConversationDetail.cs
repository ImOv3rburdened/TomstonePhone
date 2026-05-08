namespace TomestonePhone.Shared.Models;

public sealed record ConversationDetail(
    Guid Id,
    string Name,
    bool IsGroup,
    bool IsReadOnly,
    bool CanSendMessages,
    bool IsOwner,
    bool IsViewerActive,
    Guid? LinkedSupportTicketId,
    IReadOnlyList<ConversationMemberRecord> Members,
    IReadOnlyList<ConversationPendingMemberRequestRecord> PendingMemberRequests,
    IReadOnlyList<ExternalMediaEmbedRecord> Embeds);
