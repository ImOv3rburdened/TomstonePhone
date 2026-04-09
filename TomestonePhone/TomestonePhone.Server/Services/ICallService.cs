using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface ICallService
{
    Task<IReadOnlyList<CallSummary>> GetRecentCallsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveCallSessionRecord>> GetActiveCallsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<CallSummary> StartCallAsync(Guid accountId, StartCallRequest request, CancellationToken cancellationToken = default);

    Task<ActiveCallSessionRecord?> StartOrJoinActiveCallAsync(Guid accountId, StartCallRequest request, CancellationToken cancellationToken = default);

    Task<CallSummary?> EndActiveCallAsync(Guid accountId, EndActiveCallRequest request, CancellationToken cancellationToken = default);

    Task<CallSummary?> CompleteCallAsync(Guid accountId, CompleteCallRequest request, CancellationToken cancellationToken = default);

    Task<int> AcknowledgeMissedCallsAsync(Guid accountId, CancellationToken cancellationToken = default);
}
