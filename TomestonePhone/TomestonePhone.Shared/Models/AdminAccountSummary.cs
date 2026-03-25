namespace TomestonePhone.Shared.Models;

public sealed record AdminAccountSummary(
    Guid AccountId,
    string Username,
    string DisplayName,
    string PhoneNumber,
    AccountRole Role,
    AccountStatus Status,
    IReadOnlyList<string> KnownIpAddresses);
