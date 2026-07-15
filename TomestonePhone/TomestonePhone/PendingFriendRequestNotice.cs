namespace TomestonePhone;

public sealed class PendingFriendRequestNotice
{
    public Guid RequestId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}
