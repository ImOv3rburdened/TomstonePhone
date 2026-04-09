namespace TomestonePhone.Server.Models;

public sealed class PersistedAccount
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public string Status { get; set; } = "Active";

    public string PresenceStatus { get; set; } = "Available";

    public bool IsPaidMember { get; set; }

    public bool NotificationsMuted { get; set; }

    public string AcceptedLegalTermsVersion { get; set; } = string.Empty;

    public DateTimeOffset? AcceptedLegalTermsAtUtc { get; set; }

    public string AcceptedPrivacyPolicyVersion { get; set; } = string.Empty;

    public DateTimeOffset? AcceptedPrivacyPolicyAtUtc { get; set; }

    public HashSet<string> KnownIpAddresses { get; set; } = [];

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public HashSet<Guid> BlockedAccountIds { get; set; } = [];

    public PersistedGameIdentity? LastKnownGameIdentity { get; set; }

    public PersistedMediaLayout? Wallpaper { get; set; }

    public PersistedMediaLayout? Avatar { get; set; }

    public Dictionary<Guid, PersistedContactPreference> ContactPreferences { get; set; } = [];
}


