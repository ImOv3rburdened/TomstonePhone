namespace TomestonePhone.Shared.Models;

public sealed record FriendRequestCreateRequest(string PhoneNumberOrUsername, string? Message);
