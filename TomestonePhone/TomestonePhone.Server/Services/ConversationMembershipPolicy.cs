using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

internal static class ConversationMembershipPolicy
{
    public static PersistedConversationMember? FindMember(PersistedConversation conversation, Guid accountId)
    {
        return conversation.Members.SingleOrDefault(member => member.AccountId == accountId);
    }

    public static IEnumerable<PersistedConversationMember> GetActiveMembers(PersistedConversation conversation)
    {
        return conversation.Members.Where(IsActiveMember);
    }

    public static bool CanViewConversation(PersistedConversation conversation, Guid accountId)
    {
        return FindMember(conversation, accountId) is { HiddenAtUtc: null };
    }

    public static bool CanInteractWithConversation(PersistedConversation conversation, Guid accountId)
    {
        var member = FindMember(conversation, accountId);
        return member is not null
            && member.HiddenAtUtc is null
            && member.RemovedAtUtc is null
            && !conversation.IsReadOnly;
    }

    public static bool IsActiveMember(PersistedConversationMember? member)
    {
        return member is not null && member.RemovedAtUtc is null;
    }

    public static bool IsMessageVisibleToViewer(PersistedConversationMember viewerMember, PersistedMessage message)
    {
        return viewerMember.RemovedAtUtc is not { } removedAtUtc || message.SentAtUtc <= removedAtUtc;
    }

    public static GroupMemberRole ParseRole(string value)
    {
        return Enum.TryParse<GroupMemberRole>(value, out var role) ? role : GroupMemberRole.Member;
    }
}
