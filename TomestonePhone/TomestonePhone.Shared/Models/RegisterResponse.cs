namespace TomestonePhone.Shared.Models;

public sealed record RegisterResponse(Guid AccountId, string Username, string PhoneNumber, string AuthToken);
