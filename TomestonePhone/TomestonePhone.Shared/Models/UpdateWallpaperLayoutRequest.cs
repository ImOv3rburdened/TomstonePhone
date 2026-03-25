namespace TomestonePhone.Shared.Models;

public sealed record UpdateWallpaperLayoutRequest(
    float Zoom,
    float OffsetX,
    float OffsetY,
    float ViewportWidth,
    float ViewportHeight);
