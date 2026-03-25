namespace TomestonePhone.Shared.Models;

public sealed record ContactNoteUpdateRequest(Guid ContactAccountId, string DisplayName, string Note);
