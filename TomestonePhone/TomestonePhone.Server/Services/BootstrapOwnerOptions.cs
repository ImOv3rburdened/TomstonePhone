namespace TomestonePhone.Server.Services;

public sealed class BootstrapOwnerOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;
}
