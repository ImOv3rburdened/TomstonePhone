namespace TomestonePhone.Shared.Models;

public sealed record SendMessageRequest(
    Guid ConversationId,
    string Body,
    GameIdentityRecord? SenderGameIdentity = null,
    IReadOnlyList<SendMessageEmbedRequest>? Embeds = null);
