namespace TomestonePhone.Shared.Models;

public sealed record UserAvatarLayout(
    string ImageUrl,
    float Zoom,
    float OffsetX,
    float OffsetY,
    float ViewportSize,
    DateTimeOffset UpdatedAtUtc);
