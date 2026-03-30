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
            var ticket = this.CreateTicketCore(state, accountId, subject, body, true, quarantinedImagePath);
            return Map(state, ticket);
        }, cancellationToken);
    }

    public Task<SupportTicketRecord> CreateTicketAsync(Guid accountId, CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var ticket = this.CreateTicketCore(state, accountId, request.Subject, request.Body, request.IsModerationCase, string.Empty);
            return Map(state, ticket);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SupportTicketRecord>> GetTicketsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<SupportTicketRecord>>(state =>
        {
            return state.SupportTickets
                .Where(item => item.AccountId == accountId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => Map(state, item))
                .ToList();
        }, cancellationToken);
    }

    public Task<SupportTicketRecord?> AddParticipantAsync(Guid actorAccountId, Guid ticketId, Guid targetAccountId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<SupportTicketRecord?>(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            if (!SystemConversationCoordinator.IsStaffRole(actor.Role))
            {
                return null;
            }

            var ticket = state.SupportTickets.SingleOrDefault(item => item.Id == ticketId);
            if (ticket is null || ticket.Status == nameof(SupportTicketStatus.Closed))
            {
                return null;
            }

            var conversation = state.Conversations.SingleOrDefault(item => item.Id == ticket.ConversationId && !item.IsDeleted);
            if (conversation is null)
            {
                return null;
            }

            if (state.Accounts.All(item => item.Id != targetAccountId))
            {
                return null;
            }

            if (conversation.Members.All(item => item.AccountId != targetAccountId))
            {
                conversation.Members.Add(new PersistedConversationMember
                {
                    AccountId = targetAccountId,
                    Role = nameof(GroupMemberRole.Member),
                    JoinedAtUtc = DateTimeOffset.UtcNow,
                });
            }

            return Map(state, ticket);
        }, cancellationToken);
    }

    public Task<SupportTicketRecord?> CloseTicketAsync(Guid actorAccountId, Guid ticketId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<SupportTicketRecord?>(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            if (!SystemConversationCoordinator.IsStaffRole(actor.Role))
            {
                return null;
            }

            var ticket = state.SupportTickets.SingleOrDefault(item => item.Id == ticketId);
            if (ticket is null)
            {
                return null;
            }

            ticket.Status = nameof(SupportTicketStatus.Closed);
            ticket.ClosedAtUtc = DateTimeOffset.UtcNow;
            ticket.ClosedByAccountId = actorAccountId;

            var conversation = state.Conversations.SingleOrDefault(item => item.Id == ticket.ConversationId && !item.IsDeleted);
            if (conversation is not null)
            {
                conversation.IsReadOnly = true;
            }

            return Map(state, ticket);
        }, cancellationToken);
    }

    private PersistedSupportTicket CreateTicketCore(PersistedAppState state, Guid accountId, string subject, string body, bool isModerationCase, string quarantinedImagePath)
    {
        var owner = state.Accounts.Single(item => item.Id == accountId);
        var staffAccounts = SystemConversationCoordinator.GetStaffAccounts(state);
        var ownerStaffId = staffAccounts.FirstOrDefault()?.Id ?? accountId;
        var ticketId = Guid.NewGuid();
        var conversation = new PersistedConversation
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(subject) ? "Support Ticket" : subject.Trim(),
            IsGroup = true,
            Kind = SystemConversationCoordinator.SupportConversationKind,
            LinkedSupportTicketId = ticketId,
            IsReadOnly = false,
            Members = staffAccounts
                .Select(account => new PersistedConversationMember
                {
                    AccountId = account.Id,
                    Role = account.Id == ownerStaffId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member),
                    JoinedAtUtc = DateTimeOffset.UtcNow,
                })
                .ToList(),
        };

        if (conversation.Members.All(item => item.AccountId != accountId))
        {
            conversation.Members.Add(new PersistedConversationMember
            {
                AccountId = accountId,
                Role = nameof(GroupMemberRole.Member),
                JoinedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        state.Conversations.Add(conversation);

        var ticket = new PersistedSupportTicket
        {
            Id = ticketId,
            AccountId = accountId,
            ConversationId = conversation.Id,
            Subject = conversation.Name,
            Body = body,
            Status = nameof(SupportTicketStatus.Open),
            IsModerationCase = isModerationCase,
            QuarantinedImagePath = quarantinedImagePath,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        state.SupportTickets.Add(ticket);
        state.AuditLogs.Add(new PersistedAuditLog
        {
            Id = Guid.NewGuid(),
            ActorAccountId = owner.Id,
            ActorDisplayName = owner.DisplayName,
            EventType = "SupportTicketCreated",
            Summary = $"Support ticket {ticket.Subject} created with conversation {conversation.Id}.",
            CreatedAtUtc = ticket.CreatedAtUtc,
        });

        return ticket;
    }


    private static PersistedConversation EnsureTicketConversation(PersistedAppState state, PersistedSupportTicket ticket)
    {
        var owner = state.Accounts.Single(item => item.Id == ticket.AccountId);
        var staffAccounts = SystemConversationCoordinator.GetStaffAccounts(state);
        var ownerStaffId = staffAccounts.FirstOrDefault()?.Id ?? owner.Id;
        var conversation = ticket.ConversationId != Guid.Empty
            ? state.Conversations.SingleOrDefault(item => item.Id == ticket.ConversationId && !item.IsDeleted)
            : null;

        if (conversation is null)
        {
            conversation = new PersistedConversation
            {
                Id = ticket.ConversationId == Guid.Empty ? Guid.NewGuid() : ticket.ConversationId,
                Name = string.IsNullOrWhiteSpace(ticket.Subject) ? "Support Ticket" : ticket.Subject.Trim(),
                IsGroup = true,
                Kind = SystemConversationCoordinator.SupportConversationKind,
                LinkedSupportTicketId = ticket.Id,
                IsReadOnly = ticket.Status == nameof(SupportTicketStatus.Closed),
                Members = [],
            };
            state.Conversations.Add(conversation);
            ticket.ConversationId = conversation.Id;
        }
        else
        {
            conversation.Name = string.IsNullOrWhiteSpace(ticket.Subject) ? conversation.Name : ticket.Subject.Trim();
            conversation.IsGroup = true;
            conversation.Kind = SystemConversationCoordinator.SupportConversationKind;
            conversation.LinkedSupportTicketId = ticket.Id;
            conversation.IsReadOnly = ticket.Status == nameof(SupportTicketStatus.Closed);
        }

        foreach (var staff in staffAccounts)
        {
            if (conversation.Members.All(item => item.AccountId != staff.Id))
            {
                conversation.Members.Add(new PersistedConversationMember
                {
                    AccountId = staff.Id,
                    Role = staff.Id == ownerStaffId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member),
                    JoinedAtUtc = DateTimeOffset.UtcNow,
                });
            }
        }

        if (conversation.Members.All(item => item.AccountId != owner.Id))
        {
            conversation.Members.Add(new PersistedConversationMember
            {
                AccountId = owner.Id,
                Role = nameof(GroupMemberRole.Member),
                JoinedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        return conversation;
    }
    private static SupportTicketRecord Map(PersistedAppState state, PersistedSupportTicket ticket)
    {
        var owner = state.Accounts.Single(item => item.Id == ticket.AccountId);
        return new SupportTicketRecord(
            ticket.Id,
            ticket.ConversationId,
            ticket.AccountId,
            owner.DisplayName,
            ticket.Subject,
            ticket.Body,
            Enum.TryParse<SupportTicketStatus>(ticket.Status, out var status) ? status : SupportTicketStatus.Open,
            ticket.CreatedAtUtc,
            ticket.ClosedAtUtc,
            ticket.ClosedByAccountId,
            ticket.IsModerationCase);
    }
}

