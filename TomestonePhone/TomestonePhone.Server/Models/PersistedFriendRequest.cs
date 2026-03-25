namespace TomestonePhone.Server.Models;

public sealed class PersistedFriendRequest
{
    public Guid Id { get; set; }

    public Guid SenderAccountId { get; set; }

    public Guid RecipientAccountId { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Status { get; set; } = "Pending";
}
