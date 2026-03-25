namespace TomestonePhone.Server.Models;

public sealed class PersistedMediaLayout
{
    public string RelativePath { get; set; } = string.Empty;

    public float Zoom { get; set; } = 1f;

    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    public float ViewportWidth { get; set; } = 390f;

    public float ViewportHeight { get; set; } = 844f;

    public float ViewportSize { get; set; } = 128f;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
