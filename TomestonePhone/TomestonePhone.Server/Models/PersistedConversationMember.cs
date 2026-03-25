namespace TomestonePhone.Server.Models;

public sealed class PersistedConversationMember
{
    public Guid AccountId { get; set; }

    public string Role { get; set; } = "Member";

    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
