namespace TomestonePhone.Server.Models;

public sealed class PersistedExternalEmbed
{
    public Guid Id { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Kind { get; set; } = "Unknown";
}
