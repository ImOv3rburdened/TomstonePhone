namespace TomestonePhone.Server.Services;

public sealed class MariaDbOptions
{
    public string Server { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string SslMode { get; set; } = "None";

    public bool AllowPublicKeyRetrieval { get; set; } = true;
}
