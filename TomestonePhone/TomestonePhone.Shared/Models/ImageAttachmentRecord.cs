namespace TomestonePhone.Shared.Models;

public sealed record ImageAttachmentRecord(
    Guid Id,
    string ImageUrl,
    string ThumbnailUrl,
    string UploadedByDisplayName,
    DateTimeOffset UploadedAtUtc,
    bool IsRemovedFromUserView);
