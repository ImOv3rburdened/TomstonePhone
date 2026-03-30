namespace TomestonePhone.Shared.Models;

public sealed record AddSupportTicketParticipantRequest(Guid TicketId, Guid AccountId);
