namespace TomestonePhone.Shared.Models;

public sealed record WallpaperLayout(
    string ImageUrl,
    float Zoom,
    float OffsetX,
    float OffsetY,
    float ViewportWidth,
    float ViewportHeight,
    DateTimeOffset UpdatedAtUtc);
