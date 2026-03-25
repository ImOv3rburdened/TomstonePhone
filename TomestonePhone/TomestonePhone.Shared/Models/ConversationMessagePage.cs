namespace TomestonePhone.Shared.Models;

public sealed record ConversationMessagePage(Guid ConversationId, IReadOnlyList<ChatMessageRecord> Messages);
