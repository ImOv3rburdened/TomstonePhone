namespace TomestonePhone.Shared.Models;

public sealed record PhoneProfile(
    Guid AccountId,
    string Username,
    string DisplayName,
    string PhoneNumber,
    AccountRole Role,
    AccountStatus Status,
    PhonePresenceStatus PresenceStatus,
    bool NotificationsMuted,
    string AcceptedLegalTermsVersion,
    string AcceptedPrivacyPolicyVersion,
    GameIdentityRecord? LastKnownGameIdentity,
    WallpaperLayout? Wallpaper,
    UserAvatarLayout? Avatar);

