namespace TomestonePhone.Shared.Models;

public sealed record RegisterRequest(
    string Username,
    string Password,
    bool AcceptedLegalTerms,
    string LegalTermsVersion,
    DateTimeOffset AcceptedAtUtc,
    bool AcceptedPrivacyPolicy,
    string PrivacyPolicyVersion,
    DateTimeOffset AcceptedPrivacyAtUtc);
