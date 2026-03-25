namespace TomestonePhone.Shared.Models;

public sealed record AcceptLegalTermsRequest(string LegalTermsVersion, DateTimeOffset AcceptedAtUtc);
