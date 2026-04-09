using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public static class AppStateMigrator
{
    public const int LatestSchemaVersion = 10;

    public static bool Migrate(PersistedAppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        while (TryMigrateNext(state, out _, out _))
        {
            changed = true;
        }

        return changed;
    }

    public static bool TryMigrateNext(PersistedAppState state, out int fromVersion, out int toVersion)
    {
        ArgumentNullException.ThrowIfNull(state);

        fromVersion = Math.Max(0, state.SchemaVersion);
        if (state.SchemaVersion != fromVersion)
        {
            state.SchemaVersion = fromVersion;
        }

        if (state.SchemaVersion >= LatestSchemaVersion)
        {
            toVersion = state.SchemaVersion;
            return false;
        }

        if (!ApplyNextMigration(state))
        {
            throw new InvalidOperationException($"No migration step is defined from schema version {state.SchemaVersion}.");
        }

        toVersion = state.SchemaVersion;
        if (toVersion <= fromVersion)
        {
            throw new InvalidOperationException($"Migration step from schema version {fromVersion} did not advance the schema version.");
        }

        return true;
    }

    private static bool ApplyNextMigration(PersistedAppState state)
    {
        return state.SchemaVersion switch
        {
            0 => ApplyMigration1(state),
            1 => ApplyMigration2(state),
            2 => ApplyMigration3(state),
            3 => ApplyMigration4(state),
            4 => ApplyMigration5(state),
            5 => ApplyMigration6(state),
            6 => ApplyMigration7(state),
            7 => ApplyMigration8(state),
            8 => ApplyMigration9(state),
            9 => ApplyMigration10(state),
            _ => false,
        };
    }

    private static bool ApplyMigration1(PersistedAppState state)
    {
        state.Accounts ??= [];
        state.Conversations ??= [];
        state.Calls ??= [];
        state.ActiveCallSessions ??= [];
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
            account.PresenceStatus = string.IsNullOrWhiteSpace(account.PresenceStatus) ? nameof(PhonePresenceStatus.Available) : account.PresenceStatus;
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

    private static bool ApplyMigration4(PersistedAppState state)
    {
        foreach (var account in state.Accounts)
        {
            account.LastHeartbeatAtUtc ??= null;
        }

        state.SchemaVersion = 4;
        return true;
    }

    private static bool ApplyMigration5(PersistedAppState state)
    {
        foreach (var call in state.Calls)
        {
            call.VoiceProvider ??= string.Empty;
            call.VoiceHost ??= string.Empty;
            call.VoiceChannelName ??= string.Empty;
            call.VoiceAccessToken ??= string.Empty;
            call.VoiceQualityLabel ??= string.Empty;
        }

        state.SchemaVersion = 5;
        return true;
    }

    private static bool ApplyMigration6(PersistedAppState state)
    {
        state.ActiveCallSessions ??= [];
        foreach (var account in state.Accounts)
        {
            account.PresenceStatus = string.IsNullOrWhiteSpace(account.PresenceStatus) ? nameof(PhonePresenceStatus.Available) : account.PresenceStatus;
        }

        state.SchemaVersion = 6;
        return true;
    }

    private static bool ApplyMigration7(PersistedAppState state)
    {
        state.ActiveCallSessions ??= [];
        foreach (var session in state.ActiveCallSessions)
        {
            session.DisplayName ??= string.Empty;
            session.VoiceProvider ??= string.Empty;
            session.VoiceHost ??= string.Empty;
            session.VoiceChannelName ??= string.Empty;
            session.VoiceAccessToken ??= string.Empty;
            session.VoiceQualityLabel ??= string.Empty;
        }

        state.SchemaVersion = 7;
        return true;
    }

    private static bool ApplyMigration8(PersistedAppState state)
    {
        foreach (var conversation in state.Conversations)
        {
            conversation.Messages ??= [];
            foreach (var message in conversation.Messages)
            {
                message.Kind = string.IsNullOrWhiteSpace(message.Kind) ? nameof(ChatMessageKind.User) : message.Kind;
                message.Embeds ??= [];
            }
        }

        state.SchemaVersion = 8;
        return true;
    }

    private static bool ApplyMigration9(PersistedAppState state)
    {
        state.SchemaVersion = 9;
        return true;
    }

    private static bool ApplyMigration10(PersistedAppState state)
    {
        foreach (var account in state.Accounts)
        {
            account.IsPaidMember = account.IsPaidMember;
        }

        state.SchemaVersion = 10;
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


