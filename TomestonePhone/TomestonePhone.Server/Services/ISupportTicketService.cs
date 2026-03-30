using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface ISupportTicketService
{
    Task<IReadOnlyList<SupportTicketRecord>> GetTicketsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<SupportTicketRecord> CreateTicketAsync(Guid accountId, CreateSupportTicketRequest request, CancellationToken cancellationToken = default);

    Task<SupportTicketRecord> CreateModerationTicketAsync(Guid accountId, string subject, string body, string quarantinedImagePath, CancellationToken cancellationToken = default);

    Task<SupportTicketRecord?> AddParticipantAsync(Guid actorAccountId, Guid ticketId, Guid targetAccountId, CancellationToken cancellationToken = default);

    Task<SupportTicketRecord?> CloseTicketAsync(Guid actorAccountId, Guid ticketId, CancellationToken cancellationToken = default);
}
