namespace TomestonePhone.Shared.Models;

public sealed record UpdateAvatarLayoutRequest(
    float Zoom,
    float OffsetX,
    float OffsetY,
    float ViewportSize);
