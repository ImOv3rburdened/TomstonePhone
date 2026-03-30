using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public static class SystemConversationCoordinator
{
    public const string StandardConversationKind = "Standard";
    public const string StaffConversationKind = "Staff";
    public const string SupportConversationKind = "Support";
    public const string StaffConversationName = "Staff Room";

    public static bool IsStaffRole(string role)
    {
        return role is nameof(AccountRole.Owner) or nameof(AccountRole.Admin) or nameof(AccountRole.Moderator);
    }

    public static IReadOnlyList<PersistedAccount> GetStaffAccounts(PersistedAppState state)
    {
        return state.Accounts
            .Where(account => IsStaffRole(account.Role))
            .OrderBy(account => GetRoleOrder(account.Role))
            .ThenBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static PersistedConversation EnsureStaffConversation(PersistedAppState state)
    {
        var staffAccounts = GetStaffAccounts(state);
        var conversation = state.Conversations.FirstOrDefault(item => item.Kind == StaffConversationKind && !item.IsDeleted);
        if (conversation is null)
        {
            conversation = new PersistedConversation
            {
                Id = Guid.NewGuid(),
                Name = StaffConversationName,
                IsGroup = true,
                Kind = StaffConversationKind,
            };
            state.Conversations.Add(conversation);
        }

        conversation.Name = StaffConversationName;
        conversation.IsGroup = true;
        conversation.Kind = StaffConversationKind;
        conversation.IsDeleted = false;
        conversation.IsReadOnly = false;

        var allowedIds = staffAccounts.Select(account => account.Id).ToHashSet();
        conversation.Members.RemoveAll(member => !allowedIds.Contains(member.AccountId));

        foreach (var account in staffAccounts)
        {
            if (conversation.Members.All(member => member.AccountId != account.Id))
            {
                conversation.Members.Add(new PersistedConversationMember
                {
                    AccountId = account.Id,
                    Role = nameof(GroupMemberRole.Member),
                    JoinedAtUtc = DateTimeOffset.UtcNow,
                });
            }
        }

        if (conversation.Members.Count > 0)
        {
            var ownerId = staffAccounts.First().Id;
            foreach (var member in conversation.Members)
            {
                member.Role = member.AccountId == ownerId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member);
            }
        }

        return conversation;
    }

    private static int GetRoleOrder(string role)
    {
        return role switch
        {
            nameof(AccountRole.Owner) => 0,
            nameof(AccountRole.Admin) => 1,
            nameof(AccountRole.Moderator) => 2,
            _ => 9,
        };
    }
}
