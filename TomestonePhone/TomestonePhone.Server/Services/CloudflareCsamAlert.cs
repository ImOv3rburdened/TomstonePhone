namespace TomestonePhone.Server.Services;

public sealed record CloudflareCsamAlert(
    Guid AccountId,
    string ContentUrl,
    string ReportedIpAddress,
    string Reason,
    string SourceReference);
