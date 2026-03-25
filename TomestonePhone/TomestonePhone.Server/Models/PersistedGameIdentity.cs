namespace TomestonePhone.Server.Models;

public sealed class PersistedGameIdentity
{
    public string CharacterName { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;

    public string FullHandle { get; set; } = string.Empty;
}
