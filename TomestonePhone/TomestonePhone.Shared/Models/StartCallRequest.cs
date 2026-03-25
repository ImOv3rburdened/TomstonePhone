namespace TomestonePhone.Shared.Models;

public sealed record StartCallRequest(Guid ConversationId, bool IsGroup);
