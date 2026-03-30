namespace TomestonePhone.Shared.Models;

public sealed record UpdateAccountRoleRequest(Guid AccountId, AccountRole Role);
