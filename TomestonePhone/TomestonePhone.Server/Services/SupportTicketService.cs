using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class SupportTicketService : ISupportTicketService
{
    private readonly IPhoneRepository repository;

    public SupportTicketService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<SupportTicketRecord> CreateModerationTicketAsync(Guid accountId, string subject, string body, string quarantinedImagePath, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var ticket = new PersistedSupportTicket
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Subject = subject,
                Body = body,
                Status = SupportTicketStatus.Open.ToString(),
                IsModerationCase = true,
                QuarantinedImagePath = quarantinedImagePath,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            state.SupportTickets.Add(ticket);
            return Map(ticket);
        }, cancellationToken);
    }

    public Task<SupportTicketRecord> CreateTicketAsync(Guid accountId, CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var ticket = new PersistedSupportTicket
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Subject = request.Subject,
                Body = request.Body,
                Status = SupportTicketStatus.Open.ToString(),
                IsModerationCase = request.IsModerationCase,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            state.SupportTickets.Add(ticket);
            return Map(ticket);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SupportTicketRecord>> GetTicketsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<SupportTicketRecord>>(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId);
            var isStaff = account.Role is nameof(Shared.Models.AccountRole.Owner) or nameof(Shared.Models.AccountRole.Admin) or nameof(Shared.Models.AccountRole.Moderator);
            return state.SupportTickets
                .Where(item => isStaff || item.AccountId == accountId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(Map)
                .ToList();
        }, cancellationToken);
    }

    private static SupportTicketRecord Map(PersistedSupportTicket ticket)
    {
        return new SupportTicketRecord(
            ticket.Id,
            ticket.Subject,
            ticket.Body,
            Enum.TryParse<SupportTicketStatus>(ticket.Status, out var status) ? status : SupportTicketStatus.Open,
            ticket.CreatedAtUtc,
            ticket.IsModerationCase);
    }
}
