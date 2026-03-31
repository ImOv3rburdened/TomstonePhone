namespace TomestonePhone.Shared.Models;

public sealed record ServerAnnouncementRecord(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAtUtc,
    string CreatedByDisplayName);