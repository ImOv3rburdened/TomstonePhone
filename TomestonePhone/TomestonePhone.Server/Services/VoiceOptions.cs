namespace TomestonePhone.Server.Services;

public sealed class VoiceOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "Murmur";

    public string Host { get; set; } = string.Empty;

    public int TcpPort { get; set; } = 64738;

    public int UdpPort { get; set; } = 64738;

    public string QualityLabel { get; set; } = "Aether Voice (Low Bandwidth)";

    public int SampleRateHz { get; set; } = 16000;

    public int BitrateKbps { get; set; } = 16;

    public int FrameSizeMs { get; set; } = 20;
}
