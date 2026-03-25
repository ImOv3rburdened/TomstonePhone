namespace TomestonePhone.Shared.Models;

public sealed record PasswordChangeRequest(string OldPassword, string NewPassword, string ConfirmPassword);
