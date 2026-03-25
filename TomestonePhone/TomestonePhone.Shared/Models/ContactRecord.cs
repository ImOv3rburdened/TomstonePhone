namespace TomestonePhone.Shared.Models;

public sealed record ContactRecord(Guid Id, string DisplayName, string PhoneNumber, string Note);
