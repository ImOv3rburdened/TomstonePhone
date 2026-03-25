namespace TomestonePhone.Shared.Models;

public sealed record AdminPasswordResetRequest(Guid AccountId, string NewPassword);
