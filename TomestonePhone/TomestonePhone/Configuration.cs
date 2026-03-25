using System.IO;
using Dalamud.Configuration;

namespace TomestonePhone;

public sealed class Configuration : IPluginConfiguration
{
    private static readonly string AssetRoot = Path.Combine(AppContext.BaseDirectory, "images");

    public int Version { get; set; } = 1;

    public string ServerBaseUrl { get; set; } = "http://173.208.169.194:5050";

    public string? Username { get; set; }

    public string? AuthToken { get; set; }

    public string BackgroundImagePath { get; set; } = Path.Combine(AssetRoot, "phone-wallpaper-default.svg");

    public string ContactsIconPath { get; set; } = Path.Combine(AssetRoot, "app-contacts.svg");

    public string MessagesIconPath { get; set; } = Path.Combine(AssetRoot, "app-messages.svg");

    public string CallsIconPath { get; set; } = Path.Combine(AssetRoot, "app-phone.svg");

    public string FriendsIconPath { get; set; } = Path.Combine(AssetRoot, "app-friends.svg");

    public string SettingsIconPath { get; set; } = Path.Combine(AssetRoot, "app-settings.svg");

    public string LegalIconPath { get; set; } = Path.Combine(AssetRoot, "app-legal.svg");

    public string PrivacyIconPath { get; set; } = Path.Combine(AssetRoot, "app-legal.svg");

    public string SupportIconPath { get; set; } = Path.Combine(AssetRoot, "app-settings.svg");

    public string StaffIconPath { get; set; } = Path.Combine(AssetRoot, "app-settings.svg");

    public string AppIconPath { get; set; } = Path.Combine(AssetRoot, "tomestone-app-icon.svg");

    public string AccentColorHex { get; set; } = "#D9B56D";

    public string GiphyApiKey { get; set; } = string.Empty;

    public string GiphyRating { get; set; } = "pg-13";

    public bool LockViewport { get; set; } = true;

    public bool StartHidden { get; set; }

    public NotificationAnchor NotificationAnchor { get; set; } = NotificationAnchor.TopRight;

    public string AcceptedLegalTermsVersion { get; set; } = string.Empty;

    public DateTimeOffset? AcceptedLegalTermsAtUtc { get; set; }

    public string AcceptedLegalIdentity { get; set; } = string.Empty;

    public string AcceptedPrivacyPolicyVersion { get; set; } = string.Empty;

    public DateTimeOffset? AcceptedPrivacyPolicyAtUtc { get; set; }

    public bool LocalAccountLockout { get; set; }

    public string LocalAccountLockoutReason { get; set; } = string.Empty;

    public bool GiphySetupSeen { get; set; }

    public List<GifFavorite> GifFavorites { get; set; } = [];    public void NormalizeServerBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(this.ServerBaseUrl))
        {
            this.ServerBaseUrl = "http://173.208.169.194:5050";
            return;
        }

        this.ServerBaseUrl = this.ServerBaseUrl
            .Replace(":8080", ":5050", StringComparison.OrdinalIgnoreCase)
            .Replace("/8080", "/5050", StringComparison.OrdinalIgnoreCase);
    }
}



