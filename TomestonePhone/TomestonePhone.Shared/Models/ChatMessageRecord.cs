namespace TomestonePhone.Shared.Models;

public sealed record ChatMessageRecord(
    Guid Id,
    Guid ConversationId,
    string SenderDisplayName,
    GameIdentityRecord? SenderGameIdentity,
    string Body,
    DateTimeOffset SentAtUtc,
    bool IsDeletedForUsers,
    IReadOnlyList<ExternalMediaEmbedRecord> Embeds);
