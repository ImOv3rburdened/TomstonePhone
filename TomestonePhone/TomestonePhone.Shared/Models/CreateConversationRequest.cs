namespace TomestonePhone.Shared.Models;

public sealed record CreateConversationRequest(string Name, bool IsGroup, IReadOnlyList<Guid> ParticipantIds);
