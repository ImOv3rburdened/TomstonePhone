namespace TomestonePhone.Server.Models;

public sealed class PersistedFriendship
{
    public Guid Id { get; set; }

    public Guid AccountAId { get; set; }

    public Guid AccountBId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
