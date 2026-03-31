namespace TomestonePhone.Shared.Models;

public sealed record UpdateAccountStatusRequest(
    Guid AccountId,
    AccountStatus Status);