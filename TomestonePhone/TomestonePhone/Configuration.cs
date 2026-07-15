using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Configuration;

namespace TomestonePhone;

public sealed class Configuration : IPluginConfiguration
{
    public const string DefaultServerBaseUrl = "https://tomephone.cc";

    private const string EmbeddedContactsIcon = "embedded://app-contacts.png";
    private const string EmbeddedMessagesIcon = "embedded://app-messages.png";
    private const string EmbeddedCallsIcon = "embedded://app-phone.png";
    private const string EmbeddedFriendsIcon = "embedded://app-friends.png";
    private const string EmbeddedSettingsIcon = "embedded://app-settings.png";
    private const string EmbeddedWallpapersIcon = "embedded://app-wallpapers.png";
    private const string EmbeddedLegalIcon = "embedded://app-legal.png";
    private const string EmbeddedPrivacyIcon = "embedded://app-privacy.png";
    private const string EmbeddedSupportIcon = "embedded://app-support.png";
    private const string EmbeddedStaffIcon = "embedded://app-staff.png";
    private const string EmbeddedAppIcon = "embedded://icon.png";

    public int Version { get; set; } = 1;

    public string ServerBaseUrl { get; set; } = DefaultServerBaseUrl;

    public string? Username { get; set; }

    public string? AuthToken { get; set; }

    public string? PreferredVoiceInputDeviceKey { get; set; }

    public string? PreferredVoiceInputDeviceName { get; set; }

    public string? PreferredVoiceOutputDeviceKey { get; set; }

    public string? PreferredVoiceOutputDeviceName { get; set; }

    public bool ReduceVoiceBackgroundNoise { get; set; } = true;

    public float VoiceMicVolume { get; set; } = 1f;

    public float VoiceOutputVolume { get; set; } = 1f;

    public bool EnableSpellCheck { get; set; } = true;

    public string? RememberedUsername { get; set; }

    public string? RememberedPasswordProtected { get; set; }

    public string BackgroundImagePath { get; set; } = string.Empty;

    public PhoneWallpaperMode BackgroundMode { get; set; } = PhoneWallpaperMode.Fit;

    public float BackgroundZoom { get; set; } = 1f;

    public float BackgroundOffsetX { get; set; }

    public float BackgroundOffsetY { get; set; }

    public bool UseSolidBackgroundColor { get; set; }

    public string SolidBackgroundColorHex { get; set; } = "#1B2233";

    public float SolidBackgroundAlpha { get; set; } = 1f;

    public bool UseIconTint { get; set; }

    public bool UseGreyscaleBaseIcons { get; set; }

    public string IconTintColorHex { get; set; } = "#D9B56D";

    public float IconTintAlpha { get; set; } = 0.22f;

    public string ContactsIconPath { get; set; } = EmbeddedContactsIcon;

    public string MessagesIconPath { get; set; } = EmbeddedMessagesIcon;

    public string CallsIconPath { get; set; } = EmbeddedCallsIcon;

    public string FriendsIconPath { get; set; } = EmbeddedFriendsIcon;

    public string SettingsIconPath { get; set; } = EmbeddedSettingsIcon;

    public string WallpapersIconPath { get; set; } = EmbeddedWallpapersIcon;

    public string LegalIconPath { get; set; } = EmbeddedLegalIcon;

    public string PrivacyIconPath { get; set; } = EmbeddedPrivacyIcon;

    public string SupportIconPath { get; set; } = EmbeddedSupportIcon;

    public string StaffIconPath { get; set; } = EmbeddedStaffIcon;

    public string AppIconPath { get; set; } = EmbeddedAppIcon;

    public string AccentColorHex { get; set; } = "#D9B56D";

    public string GiphyApiKey { get; set; } = string.Empty;

    public string KlipyApiKey { get; set; } = string.Empty;

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

    public bool ShareGameIdentity { get; set; }

    public bool GiphySetupSeen { get; set; }

    public List<GifFavorite> GifFavorites { get; set; } = [];

    public List<Guid> SeenAnnouncementIds { get; set; } = [];

    public List<Guid> SeenIncomingFriendRequestIds { get; set; } = [];

    public List<PendingFriendRequestNotice> PendingOutgoingFriendRequestNotices { get; set; } = [];

    public Guid FriendNotificationAccountId { get; set; }

    public Guid ConversationNotificationAccountId { get; set; }

    public Dictionary<Guid, DateTimeOffset> KnownConversationActivityUtc { get; set; } = [];

    public List<string> HomeAppOrder { get; set; } = [];

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

    public string GetLocalWallpaperDirectory()
    {
        var path = Path.Combine(this.GetLocalUserAssetDirectory(), "wallpapers");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetLocalWallpaperImportPath(string sourcePath)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var safeName = string.Concat(sourceName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "wallpaper";
        }

        var directory = this.GetLocalWallpaperDirectory();
        var candidate = Path.Combine(directory, $"{safeName}.png");
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{safeName}-{index}.png");
            index++;
        }

        return candidate;
    }

    public string GetLocalIconDirectory()
    {
        var path = Path.Combine(this.GetLocalUserAssetDirectory(), "icons");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetLocalIconImportPath(string appId, string sourcePath)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var safeAppId = string.Concat(appId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        var safeName = string.Concat(sourceName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(safeAppId))
        {
            safeAppId = "app";
        }

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "icon";
        }

        var directory = this.GetLocalIconDirectory();
        var candidate = Path.Combine(directory, $"{safeAppId}-{safeName}.png");
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{safeAppId}-{safeName}-{index}.png");
            index++;
        }

        return candidate;
    }

    public void NormalizeServerBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(this.ServerBaseUrl))
        {
            this.ServerBaseUrl = DefaultServerBaseUrl;
            return;
        }

        var normalized = this.ServerBaseUrl.Trim()
            .Replace(":8080", ":5050", StringComparison.OrdinalIgnoreCase)
            .Replace("/8080", "/5050", StringComparison.OrdinalIgnoreCase);

        if (!TryValidateBackendUrl(normalized, out _, out _))
        {
            normalized = DefaultServerBaseUrl;
        }

        this.ServerBaseUrl = normalized.TrimEnd('/');
    }

    public static bool TryValidateBackendUrl(string value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            error = "Server URL must be an absolute HTTPS URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Server URL must use HTTPS.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out _)
            || uri.HostNameType != UriHostNameType.Dns)
        {
            error = "Server URL must use a DNS hostname, not an IP address.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(uri.PathAndQuery)
            && uri.PathAndQuery != "/")
        {
            error = "Server URL must point to the server root, for example https://tomephone.cc.";
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    public void NormalizeAssetPaths()
    {
        this.ContactsIconPath = EmbeddedContactsIcon;
        this.MessagesIconPath = EmbeddedMessagesIcon;
        this.CallsIconPath = EmbeddedCallsIcon;
        this.FriendsIconPath = EmbeddedFriendsIcon;
        this.SettingsIconPath = EmbeddedSettingsIcon;
        this.WallpapersIconPath = EmbeddedWallpapersIcon;
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

public enum PhoneWallpaperMode
{
    Fit,
    Stretch,
    Custom,
}
