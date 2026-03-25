namespace TomestonePhone.Shared.Models;

public sealed record FriendshipRecord(Guid AccountId, Guid FriendAccountId, string FriendDisplayName, string FriendPhoneNumber, DateTimeOffset SinceUtc);
