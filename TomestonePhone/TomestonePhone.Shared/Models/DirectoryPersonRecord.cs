namespace TomestonePhone.Shared.Models;

public sealed record DirectoryPersonRecord(
    Guid AccountId,
    string Username,
    string DisplayName,
    string PhoneNumber);
