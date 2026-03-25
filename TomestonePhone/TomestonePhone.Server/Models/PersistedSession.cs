namespace TomestonePhone.Server.Models;

public sealed class PersistedSession
{
    public string Token { get; set; } = string.Empty;

    public Guid AccountId { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}
