namespace TomestonePhone.Shared.Models;

public sealed record ConversationMemberRecord(Guid AccountId, string DisplayName, string PhoneNumber, GroupMemberRole Role, DateTimeOffset JoinedAtUtc);
