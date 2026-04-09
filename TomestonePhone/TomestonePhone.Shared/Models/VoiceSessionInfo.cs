namespace TomestonePhone.Shared.Models;

public sealed record VoiceSessionInfo(
    string Provider,
    string Host,
    int TcpPort,
    int UdpPort,
    string ChannelName,
    string AccessToken,
    string QualityLabel,
    int SampleRateHz,
    int BitrateKbps,
    int FrameSizeMs);
