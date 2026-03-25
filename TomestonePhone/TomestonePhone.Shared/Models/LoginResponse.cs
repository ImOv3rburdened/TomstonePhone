namespace TomestonePhone.Shared.Models;

public sealed record LoginResponse(
    Guid AccountId,
    string Username,
    string PhoneNumber,
    string AuthToken,
    DateTimeOffset ExpiresAtUtc);
