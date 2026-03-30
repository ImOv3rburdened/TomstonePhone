using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public static class AppStateMigrator
{
    public static bool Migrate(PersistedAppState state)
    {
        var changed = false;

        state.Accounts ??= [];
        state.Conversations ??= [];
        state.Calls ??= [];
        state.FriendRequests ??= [];
        state.Friendships ??= [];
        state.Reports ??= [];
        state.AuditLogs ??= [];
        state.IpBans ??= [];
        state.SupportTickets ??= [];
        state.Sessions ??= [];

        foreach (var account in state.Accounts)
        {
            account.KnownIpAddresses ??= [];
            account.BlockedAccountIds ??= [];
            account.ContactPreferences ??= [];
            changed |= string.IsNullOrWhiteSpace(account.Role) || string.IsNullOrWhiteSpace(account.Status);
            account.Role = string.IsNullOrWhiteSpace(account.Role) ? nameof(AccountRole.User) : account.Role;
            account.Status = string.IsNullOrWhiteSpace(account.Status) ? nameof(AccountStatus.Active) : account.Status;
        }

        foreach (var conversation in state.Conversations)
        {
            conversation.Kind = string.IsNullOrWhiteSpace(conversation.Kind)
                ? (conversation.LinkedSupportTicketId is not null ? SystemConversationCoordinator.SupportConversationKind : SystemConversationCoordinator.StandardConversationKind)
                : conversation.Kind;
            conversation.Members ??= [];
            conversation.Messages ??= [];
            foreach (var member in conversation.Members)
            {
                member.Role = string.IsNullOrWhiteSpace(member.Role) ? nameof(GroupMemberRole.Member) : member.Role;
            }

            foreach (var message in conversation.Messages)
            {
                message.Body ??= string.Empty;
                message.Embeds ??= [];
            }
        }

        var knownSupportConversationIds = state.SupportTickets
            .Where(ticket => ticket.ConversationId != Guid.Empty)
            .Select(ticket => ticket.ConversationId)
            .ToHashSet();

        foreach (var conversation in state.Conversations)
        {
            if (conversation.Kind == SystemConversationCoordinator.SupportConversationKind && conversation.LinkedSupportTicketId is null)
            {
                var linkedTicket = state.SupportTickets.FirstOrDefault(ticket => ticket.ConversationId == conversation.Id);
                if (linkedTicket is not null)
                {
                    conversation.LinkedSupportTicketId = linkedTicket.Id;
                    changed = true;
                }
            }
            else if (conversation.LinkedSupportTicketId is not null && conversation.Kind != SystemConversationCoordinator.SupportConversationKind)
            {
                conversation.Kind = SystemConversationCoordinator.SupportConversationKind;
                changed = true;
            }
        }

        foreach (var ticket in state.SupportTickets)
        {
            ticket.Subject ??= string.Empty;
            ticket.Body ??= string.Empty;
            ticket.Status = string.IsNullOrWhiteSpace(ticket.Status) ? nameof(SupportTicketStatus.Open) : ticket.Status;
            var beforeConversationId = ticket.ConversationId;
            var conversation = EnsureTicketConversation(state, ticket);
            changed |= beforeConversationId != ticket.ConversationId || conversation.LinkedSupportTicketId != ticket.Id;
        }

        var beforeStaffConversationCount = state.Conversations.Count(item => item.Kind == SystemConversationCoordinator.StaffConversationKind && !item.IsDeleted);
        SystemConversationCoordinator.EnsureStaffConversation(state);
        changed |= state.Conversations.Count(item => item.Kind == SystemConversationCoordinator.StaffConversationKind && !item.IsDeleted) != beforeStaffConversationCount;

        return changed;
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
                Messages = [],
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
            conversation.Members ??= [];
            conversation.Messages ??= [];
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
}

