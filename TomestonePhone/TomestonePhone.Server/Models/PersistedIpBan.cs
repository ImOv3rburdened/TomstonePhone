namespace TomestonePhone.Server.Models;

public sealed class PersistedIpBan
{
    public Guid Id { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
