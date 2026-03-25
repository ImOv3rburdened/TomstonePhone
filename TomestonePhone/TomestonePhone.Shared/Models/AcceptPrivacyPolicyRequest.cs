namespace TomestonePhone.Shared.Models;

public sealed record AcceptPrivacyPolicyRequest(string PrivacyPolicyVersion, DateTimeOffset AcceptedAtUtc);
