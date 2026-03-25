namespace TomestonePhone.Shared.Models;

public sealed record ConversationDetail(
    Guid Id,
    string Name,
    bool IsGroup,
    IReadOnlyList<ConversationMemberRecord> Members,
    IReadOnlyList<ExternalMediaEmbedRecord> Embeds);
