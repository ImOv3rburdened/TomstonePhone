using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class CallService : ICallService
{
    private readonly IPhoneRepository repository;

    public CallService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<CallSummary?> CompleteCallAsync(Guid accountId, CompleteCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<CallSummary?>(state =>
        {
            var call = state.Calls.SingleOrDefault(item => item.Id == request.CallId);
            if (call is null)
            {
                return null;
            }

            call.DurationSeconds = request.DurationSeconds;
            call.Missed = request.Missed;
            return new CallSummary(call.Id, call.DisplayName, call.IsGroup ? CallKind.Group : CallKind.Direct, call.StartedUtc, TimeSpan.FromSeconds(call.DurationSeconds), call.Missed);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CallSummary>> GetRecentCallsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<CallSummary>>(state =>
        {
            return state.Calls
                .OrderByDescending(item => item.StartedUtc)
                .Select(item => new CallSummary(item.Id, item.DisplayName, item.IsGroup ? CallKind.Group : CallKind.Direct, item.StartedUtc, TimeSpan.FromSeconds(item.DurationSeconds), item.Missed))
                .ToList();
        }, cancellationToken);
    }

    public Task<CallSummary> StartCallAsync(Guid accountId, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var conversation = state.Conversations.Single(item => item.Id == request.ConversationId);
            var caller = state.Accounts.Single(item => item.Id == accountId);
            var hasBlock = conversation.Members
                .Select(item => state.Accounts.Single(account => account.Id == item.AccountId))
                .Any(account => account.Id != accountId && (account.BlockedAccountIds.Contains(accountId) || caller.BlockedAccountIds.Contains(account.Id)));
            var call = new PersistedCall
            {
                Id = Guid.NewGuid(),
                ConversationId = request.ConversationId,
                DisplayName = conversation.Name,
                IsGroup = request.IsGroup,
                StartedUtc = DateTimeOffset.UtcNow,
                DurationSeconds = hasBlock ? 30 : 0,
                Missed = hasBlock,
            };

            state.Calls.Add(call);
            return new CallSummary(call.Id, call.DisplayName, call.IsGroup ? CallKind.Group : CallKind.Direct, call.StartedUtc, TimeSpan.FromSeconds(call.DurationSeconds), call.Missed);
        }, cancellationToken);
    }
}
