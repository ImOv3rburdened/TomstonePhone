namespace TomestonePhone.Shared.Models;

public sealed record ConversationModerationRequest(
    Guid ConversationId,
    ChatModerationAction Action,
    Guid? TargetAccountId);
