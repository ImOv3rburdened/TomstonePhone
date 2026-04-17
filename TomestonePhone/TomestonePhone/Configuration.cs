using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Configuration;

namespace TomestonePhone;

public sealed class Configuration : IPluginConfiguration
{
    private const string EmbeddedContactsIcon = "embedded://app-contacts.png";
    private const string EmbeddedMessagesIcon = "embedded://app-messages.png";
    private const string EmbeddedCallsIcon = "embedded://app-phone.png";
    private const string EmbeddedFriendsIcon = "embedded://app-friends.png";
    private const string EmbeddedSettingsIcon = "embedded://app-settings.png";
    private const string EmbeddedLegalIcon = "embedded://app-legal.png";
    private const string EmbeddedPrivacyIcon = "embedded://app-privacy.png";
    private const string EmbeddedSupportIcon = "embedded://app-support.png";
    private const string EmbeddedStaffIcon = "embedded://app-staff.png";
    private const string EmbeddedAppIcon = "embedded://icon.png";

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

    public string ContactsIconPath { get; set; } = EmbeddedContactsIcon;

    public string MessagesIconPath { get; set; } = EmbeddedMessagesIcon;

    public string CallsIconPath { get; set; } = EmbeddedCallsIcon;

    public string FriendsIconPath { get; set; } = EmbeddedFriendsIcon;

    public string SettingsIconPath { get; set; } = EmbeddedSettingsIcon;

    public string LegalIconPath { get; set; } = EmbeddedLegalIcon;

    public string PrivacyIconPath { get; set; } = EmbeddedPrivacyIcon;

    public string SupportIconPath { get; set; } = EmbeddedSupportIcon;

    public string StaffIconPath { get; set; } = EmbeddedStaffIcon;

    public string AppIconPath { get; set; } = EmbeddedAppIcon;

    public string AccentColorHex { get; set; } = "#D9B56D";

    public string GiphyApiKey { get; set; } = string.Empty;

    public string GiphyRating { get; set; } = "pg-13";

    public bool LockViewport { get; set; } = false;

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

    public void NormalizeAssetPaths()
    {
        this.ContactsIconPath = EmbeddedContactsIcon;
        this.MessagesIconPath = EmbeddedMessagesIcon;
        this.CallsIconPath = EmbeddedCallsIcon;
        this.FriendsIconPath = EmbeddedFriendsIcon;
        this.SettingsIconPath = EmbeddedSettingsIcon;
        this.LegalIconPath = EmbeddedLegalIcon;
        this.PrivacyIconPath = EmbeddedPrivacyIcon;
        this.SupportIconPath = EmbeddedSupportIcon;
        this.StaffIconPath = EmbeddedStaffIcon;
        this.AppIconPath = EmbeddedAppIcon;
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
