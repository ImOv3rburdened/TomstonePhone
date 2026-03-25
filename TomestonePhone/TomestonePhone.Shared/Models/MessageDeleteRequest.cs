namespace TomestonePhone.Shared.Models;

public sealed record MessageDeleteRequest(Guid ConversationId, Guid MessageId, string Reason);
