using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class FriendService : IFriendService
{
    private readonly IPhoneRepository repository;

    public FriendService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<FriendRequestRecord> CreateRequestAsync(Guid senderAccountId, FriendRequestCreateRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var target = state.Accounts.Single(item =>
                item.Username.Equals(request.PhoneNumberOrUsername, StringComparison.OrdinalIgnoreCase)
                || item.PhoneNumber == request.PhoneNumberOrUsername);

            var record = new PersistedFriendRequest
            {
                Id = Guid.NewGuid(),
                SenderAccountId = senderAccountId,
                RecipientAccountId = target.Id,
                Message = request.Message ?? string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Status = FriendRequestStatus.Pending.ToString(),
            };

            state.FriendRequests.Add(record);
            var sender = state.Accounts.Single(item => item.Id == senderAccountId);
            return new FriendRequestRecord(record.Id, AccountLabelFormatter.GetDisplayName(sender), sender.PhoneNumber, FriendRequestStatus.Pending);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FriendRequestRecord>> GetRequestsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<FriendRequestRecord>>(state =>
        {
            return state.FriendRequests
                .Where(item => item.RecipientAccountId == accountId)
                .Select(item =>
                {
                    var sender = state.Accounts.SingleOrDefault(account => account.Id == item.SenderAccountId);
                    return new FriendRequestRecord(
                        item.Id,
                        sender is null ? "Unknown" : AccountLabelFormatter.GetDisplayName(sender),
                        sender?.PhoneNumber ?? "0000000000",
                        Enum.TryParse<FriendRequestStatus>(item.Status, out var status) ? status : FriendRequestStatus.Pending);
                })
                .OrderByDescending(item => item.DisplayName)
                .ToList();
        }, cancellationToken);
    }

    public Task<FriendRequestRecord?> RespondAsync(Guid accountId, RespondFriendRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<FriendRequestRecord?>(state =>
        {
            var record = state.FriendRequests.SingleOrDefault(item => item.Id == request.RequestId && item.RecipientAccountId == accountId);
            if (record is null)
            {
                return null;
            }

            record.Status = request.Accept ? FriendRequestStatus.Accepted.ToString() : FriendRequestStatus.Declined.ToString();
            var sender = state.Accounts.SingleOrDefault(item => item.Id == record.SenderAccountId);

            if (request.Accept && sender is not null)
            {
                var recipient = state.Accounts.Single(item => item.Id == accountId);
                if (state.Friendships.All(item => !MatchesFriendship(item, sender.Id, recipient.Id)))
                {
                    state.Friendships.Add(new PersistedFriendship
                    {
                        Id = Guid.NewGuid(),
                        AccountAId = sender.Id,
                        AccountBId = recipient.Id,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }

                recipient.ContactPreferences[sender.Id] = new PersistedContactPreference
                {
                    DisplayName = AccountLabelFormatter.GetDisplayName(sender),
                    Note = string.Empty,
                };

                sender.ContactPreferences[recipient.Id] = new PersistedContactPreference
                {
                    DisplayName = AccountLabelFormatter.GetDisplayName(recipient),
                    Note = string.Empty,
                };
            }

            return new FriendRequestRecord(
                record.Id,
                sender?.DisplayName ?? "Unknown",
                sender?.PhoneNumber ?? "0000000000",
                request.Accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Declined);
        }, cancellationToken);
    }

    public Task<bool> RemoveFriendshipAsync(Guid accountId, RemoveFriendRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var removed = state.Friendships.RemoveAll(item => MatchesFriendship(item, accountId, request.FriendAccountId)) > 0;
            if (!removed)
            {
                return false;
            }

            var actor = state.Accounts.Single(item => item.Id == accountId);
            var other = state.Accounts.SingleOrDefault(item => item.Id == request.FriendAccountId);
            actor.ContactPreferences.Remove(request.FriendAccountId);
            if (other is not null)
            {
                other.ContactPreferences.Remove(accountId);
            }

            return true;
        }, cancellationToken);
    }

    private static bool MatchesFriendship(PersistedFriendship friendship, Guid a, Guid b)
    {
        return (friendship.AccountAId == a && friendship.AccountBId == b)
            || (friendship.AccountAId == b && friendship.AccountBId == a);
    }
}
