using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

internal static class AccountLabelFormatter
{
    public static bool IsInactive(PersistedAccount? account)
    {
        return account is not null
            && string.Equals(account.Status, nameof(AccountStatus.Inactive), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnavailable(PersistedAccount? account)
    {
        return account is null
            || string.Equals(account.Status, nameof(AccountStatus.Inactive), StringComparison.OrdinalIgnoreCase)
            || string.Equals(account.Status, nameof(AccountStatus.Banned), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisplayName(PersistedAccount? account)
    {
        if (account is null)
        {
            return "Unknown";
        }

        var displayName = GetBaseDisplayName(account);
        return IsInactive(account)
            ? $"{displayName} (Deactivated account)"
            : displayName;
    }

    public static string GetContactDisplayName(PersistedAccount? account, string? preferredDisplayName)
    {
        if (account is null)
        {
            return string.IsNullOrWhiteSpace(preferredDisplayName) ? "Unknown" : preferredDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(preferredDisplayName))
        {
            var trimmed = preferredDisplayName.Trim();
            if (!trimmed.Equals(account.Username, StringComparison.OrdinalIgnoreCase)
                && !trimmed.Equals(account.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return IsInactive(account) ? $"{trimmed} (Deactivated account)" : trimmed;
            }
        }

        return GetDisplayName(account);
    }

    public static PersistedAccount ResolveAccount(IReadOnlyCollection<PersistedAccount> accounts, string lookup)
    {
        if (string.IsNullOrWhiteSpace(lookup))
        {
            throw new InvalidOperationException("Enter a username, phone number, or character name.");
        }

        var trimmedLookup = lookup.Trim();
        var exactMatches = accounts
            .Where(item =>
                item.Username.Equals(trimmedLookup, StringComparison.OrdinalIgnoreCase)
                || item.PhoneNumber.Equals(trimmedLookup, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Equals(trimmedLookup, StringComparison.OrdinalIgnoreCase)
                || (item.LastKnownGameIdentity is not null && (
                    item.LastKnownGameIdentity.CharacterName.Equals(trimmedLookup, StringComparison.OrdinalIgnoreCase)
                    || item.LastKnownGameIdentity.FullHandle.Equals(trimmedLookup, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (exactMatches.Count == 1)
        {
            return exactMatches[0];
        }

        if (exactMatches.Count > 1)
        {
            throw new InvalidOperationException("More than one account matched that name. Use the username or phone number instead.");
        }

        throw new InvalidOperationException("No account matched that username, phone number, or character name.");
    }

    private static string GetBaseDisplayName(PersistedAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.LastKnownGameIdentity?.CharacterName))
        {
            return account.LastKnownGameIdentity.CharacterName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
        {
            return account.DisplayName;
        }

        return string.IsNullOrWhiteSpace(account.Username) ? "Unknown" : account.Username;
    }
}
