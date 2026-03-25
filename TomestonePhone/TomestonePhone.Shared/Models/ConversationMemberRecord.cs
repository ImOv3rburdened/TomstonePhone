namespace TomestonePhone.Shared.Models;

public sealed record ConversationMemberRecord(Guid AccountId, string DisplayName, GroupMemberRole Role, DateTimeOffset JoinedAtUtc);
