namespace TomestonePhone.Shared.Models;

public sealed record UpsertServerAnnouncementRequest(
    string Title,
    string Body);