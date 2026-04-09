namespace TomestonePhone.Server.Models;

public sealed class PersistedMessage
{
    public Guid Id { get; set; }

    public Guid SenderAccountId { get; set; }

    public string Body { get; set; } = string.Empty;

    public PersistedGameIdentity? SenderGameIdentity { get; set; }

    public string SenderPhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset SentAtUtc { get; set; }

    public bool IsDeletedForUsers { get; set; }

    public List<PersistedExternalEmbed> Embeds { get; set; } = [];

    public string Kind { get; set; } = nameof(TomestonePhone.Shared.Models.ChatMessageKind.User);

    public Guid? RelatedCallId { get; set; }

    public int? RelatedCallDurationSeconds { get; set; }
}
