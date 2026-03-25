namespace TomestonePhone.Shared.Models;

public sealed record CompleteCallRequest(Guid CallId, int DurationSeconds, bool Missed);
