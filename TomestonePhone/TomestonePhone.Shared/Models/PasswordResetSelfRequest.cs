namespace TomestonePhone.Shared.Models;

public sealed record PasswordResetSelfRequest(string OldPassword, string NewPassword, string ConfirmPassword);
