namespace TomestonePhone.Server.Services;

public sealed class MariaDbOptions
{
    public string Server { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = "TomestonePhone";

    public string Username { get; set; } = "TomestonePhone";

    public string Password { get; set; } = "8buS~kuw6rHd";

    public string SslMode { get; set; } = "None";

    public bool AllowPublicKeyRetrieval { get; set; } = true;
}
