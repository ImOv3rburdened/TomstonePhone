namespace TomestonePhone.Shared.Models;

public sealed record ExternalMediaEmbedRecord(
    Guid Id,
    string Url,
    ExternalEmbedKind Kind,
    string PreviewLabel);
