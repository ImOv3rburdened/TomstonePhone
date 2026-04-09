using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Configuration;

namespace TomestonePhone;

public sealed class Configuration : IPluginConfiguration
{
    private static readonly string AssetRoot = Path.Combine(AppContext.BaseDirectory, "images");

    public int Version { get; set; } = 1;

    public string ServerBaseUrl { get; set; } = "http://173.208.169.194:5050";

    public string? Username { get; set; }

    public string? AuthToken { get; set; }

    public string? RememberedUsername { get; set; }

    public string? RememberedPasswordProtected { get; set; }

    public string BackgroundImagePath { get; set; } = string.Empty;

    public float BackgroundZoom { get; set; } = 1f;

    public float BackgroundOffsetX { get; set; }

    public float BackgroundOffsetY { get; set; }

    public string ContactsIconPath { get; set; } = "embedded://app-contacts.png";

    public string MessagesIconPath { get; set; } = "embedded://app-messages.png";

    public string CallsIconPath { get; set; } = "embedded://app-phone.png";

    public string FriendsIconPath { get; set; } = "embedded://app-friends.png";

    public string SettingsIconPath { get; set; } = "embedded://app-settings.png";

    public string LegalIconPath { get; set; } = "embedded://app-legal.png";

    public string PrivacyIconPath { get; set; } = "embedded://app-privacy.png";

    public string SupportIconPath { get; set; } = "embedded://app-support.png";

    public string StaffIconPath { get; set; } = "embedded://app-staff.png";

    public string AppIconPath { get; set; } = Path.Combine(AssetRoot, "tomestone-app-icon.png");

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

    public bool PlayOpenEmote { get; set; }

    public bool OpenEmoteSetupSeen { get; set; }

    public bool GiphySetupSeen { get; set; }

    public List<GifFavorite> GifFavorites { get; set; } = [];

    public List<Guid> SeenAnnouncementIds { get; set; } = [];

    public string GetLocalUserAssetDirectory()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TomestonePhone");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetLocalWallpaperPath()
    {
        return Path.Combine(this.GetLocalUserAssetDirectory(), "wallpaper.png");
    }

    public void NormalizeServerBaseUrl()
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

    public void StoreRememberedCredentials(string username, string password)
    {
        this.RememberedUsername = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        this.RememberedPasswordProtected = string.IsNullOrWhiteSpace(password) ? null : ProtectString(password);
    }

    public bool TryGetRememberedCredentials(out string username, out string password)
    {
        username = this.RememberedUsername ?? string.Empty;
        password = UnprotectString(this.RememberedPasswordProtected) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
    }

    public void ClearRememberedCredentials()
    {
        this.RememberedUsername = null;
        this.RememberedPasswordProtected = null;
    }

    private static string? ProtectString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? UnprotectString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(value);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}


