namespace TomestonePhone.Server.Models;

public sealed class PersistedActiveCallSession
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid CallId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsGroup { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public Guid StartedByAccountId { get; set; }

    public List<Guid> ParticipantAccountIds { get; set; } = [];

    public string VoiceProvider { get; set; } = string.Empty;

    public string VoiceHost { get; set; } = string.Empty;

    public int VoiceTcpPort { get; set; }

    public int VoiceUdpPort { get; set; }

    public string VoiceChannelName { get; set; } = string.Empty;

    public string VoiceAccessToken { get; set; } = string.Empty;

    public string VoiceQualityLabel { get; set; } = string.Empty;

    public int VoiceSampleRateHz { get; set; }

    public int VoiceBitrateKbps { get; set; }

    public int VoiceFrameSizeMs { get; set; }
}
