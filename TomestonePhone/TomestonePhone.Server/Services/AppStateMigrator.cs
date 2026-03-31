using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public static class AppStateMigrator
{
    public const int LatestSchemaVersion = 3;

    public static bool Migrate(PersistedAppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        var currentVersion = Math.Max(0, state.SchemaVersion);
        if (state.SchemaVersion != currentVersion)
        {
            state.SchemaVersion = currentVersion;
            changed = true;
        }

        while (state.SchemaVersion < LatestSchemaVersion)
        {
            changed |= ApplyNextMigration(state);
        }

        return changed;
    }

    private static bool ApplyNextMigration(PersistedAppState state)
    {
        return state.SchemaVersion switch
        {
            0 => ApplyMigration1(state),
            1 => ApplyMigration2(state),
            2 => ApplyMigration3(state),
            _ => false,
        };
    }

    private static bool ApplyMigration1(PersistedAppState state)
    {
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
        state.ActiveAnnouncement ??= null;

        foreach (var account in state.Accounts)
        {
            account.KnownIpAddresses ??= [];
            account.BlockedAccountIds ??= [];
            account.ContactPreferences ??= [];
            account.Role = string.IsNullOrWhiteSpace(account.Role) ? nameof(AccountRole.User) : account.Role;
            account.Status = string.IsNullOrWhiteSpace(account.Status) ? nameof(AccountStatus.Active) : account.Status;
        }

        foreach (var conversation in state.Conversations)
        {
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

        state.SchemaVersion = 1;
        return true;
    }

    private static bool ApplyMigration2(PersistedAppState state)
    {
        foreach (var conversation in state.Conversations)
        {
            var expectedKind = conversation.LinkedSupportTicketId is not null
                ? SystemConversationCoordinator.SupportConversationKind
                : SystemConversationCoordinator.StandardConversationKind;

            conversation.Kind = string.IsNullOrWhiteSpace(conversation.Kind)
                ? expectedKind
                : conversation.Kind;
        }

        foreach (var conversation in state.Conversations)
        {
            if (conversation.Kind == SystemConversationCoordinator.SupportConversationKind && conversation.LinkedSupportTicketId is null)
            {
                var linkedTicket = state.SupportTickets.FirstOrDefault(ticket => ticket.ConversationId == conversation.Id);
                if (linkedTicket is not null)
                {
                    conversation.LinkedSupportTicketId = linkedTicket.Id;
                }
            }
            else if (conversation.LinkedSupportTicketId is not null && conversation.Kind != SystemConversationCoordinator.SupportConversationKind)
            {
                conversation.Kind = SystemConversationCoordinator.SupportConversationKind;
            }
        }

        foreach (var ticket in state.SupportTickets)
        {
            ticket.Subject ??= string.Empty;
            ticket.Body ??= string.Empty;
            ticket.Status = string.IsNullOrWhiteSpace(ticket.Status) ? nameof(SupportTicketStatus.Open) : ticket.Status;
            _ = EnsureTicketConversation(state, ticket);
        }

        state.SchemaVersion = 2;
        return true;
    }

    private static bool ApplyMigration3(PersistedAppState state)
    {
        SystemConversationCoordinator.EnsureStaffConversation(state);
        state.ActiveAnnouncement ??= null;
        state.SchemaVersion = 3;
        return true;
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
