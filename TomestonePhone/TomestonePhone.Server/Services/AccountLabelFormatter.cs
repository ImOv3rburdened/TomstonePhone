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

        return IsInactive(account)
            ? $"{account.DisplayName} (Deactivated account)"
            : account.DisplayName;
    }
}
