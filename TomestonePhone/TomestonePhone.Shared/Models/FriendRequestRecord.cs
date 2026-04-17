namespace TomestonePhone.Shared.Models;

public sealed record FriendRequestRecord(Guid Id, string DisplayName, string PhoneNumber, FriendRequestStatus Status, bool IsIncoming);
