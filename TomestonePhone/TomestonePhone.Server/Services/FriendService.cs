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
            var sender = state.Accounts.Single(item => item.Id == senderAccountId);
            var target = AccountLabelFormatter.ResolveAccount(state.Accounts, request.PhoneNumberOrUsername);

            if (target.Id == senderAccountId)
            {
                throw new InvalidOperationException("You cannot send a friend request to yourself.");
            }

            if (state.Friendships.Any(item => MatchesFriendship(item, senderAccountId, target.Id)))
            {
                throw new InvalidOperationException("You are already friends.");
            }

            if (state.FriendRequests.Any(item =>
                    ((item.SenderAccountId == senderAccountId && item.RecipientAccountId == target.Id)
                    || (item.SenderAccountId == target.Id && item.RecipientAccountId == senderAccountId))
                    && string.Equals(item.Status, FriendRequestStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A pending friend request already exists.");
            }

            RemoveRequestsBetween(state, senderAccountId, target.Id);

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
            return new FriendRequestRecord(record.Id, AccountLabelFormatter.GetDisplayName(target), target.PhoneNumber, FriendRequestStatus.Pending, false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FriendRequestRecord>> GetRequestsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<FriendRequestRecord>>(state =>
        {
            return state.FriendRequests
                .Where(item => string.Equals(item.Status, FriendRequestStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase))
                .Where(item => item.RecipientAccountId == accountId || item.SenderAccountId == accountId)
                .Select(item =>
                {
                    var isIncoming = item.RecipientAccountId == accountId;
                    var otherAccountId = isIncoming ? item.SenderAccountId : item.RecipientAccountId;
                    var otherAccount = state.Accounts.SingleOrDefault(account => account.Id == otherAccountId);
                    return new FriendRequestRecord(
                        item.Id,
                        otherAccount is null ? "Unknown" : AccountLabelFormatter.GetDisplayName(otherAccount),
                        otherAccount?.PhoneNumber ?? "0000000000",
                        Enum.TryParse<FriendRequestStatus>(item.Status, out var status) ? status : FriendRequestStatus.Pending,
                        isIncoming);
                })
                .OrderByDescending(item => item.Status == FriendRequestStatus.Pending)
                .ThenBy(item => item.DisplayName)
                .ToList();
        }, cancellationToken);
    }

    public Task<FriendRequestRecord?> RespondAsync(Guid accountId, RespondFriendRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<FriendRequestRecord?>(state =>
        {
            var record = state.FriendRequests.SingleOrDefault(item =>
                item.Id == request.RequestId
                && item.RecipientAccountId == accountId
                && string.Equals(item.Status, FriendRequestStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return null;
            }

            var sender = state.Accounts.SingleOrDefault(item => item.Id == record.SenderAccountId);
            var response = new FriendRequestRecord(
                record.Id,
                sender is null ? "Unknown" : AccountLabelFormatter.GetDisplayName(sender),
                sender?.PhoneNumber ?? "0000000000",
                request.Accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Declined,
                true);

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

                UpsertFriendContact(recipient, sender);
                UpsertFriendContact(sender, recipient);
            }

            RemoveRequestsBetween(state, record.SenderAccountId, record.RecipientAccountId);
            return response;
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

            RemoveRequestsBetween(state, accountId, request.FriendAccountId);

            return true;
        }, cancellationToken);
    }

    private static void UpsertFriendContact(PersistedAccount owner, PersistedAccount friend)
    {
        owner.ContactPreferences[friend.Id] = new PersistedContactPreference
        {
            DisplayName = AccountLabelFormatter.GetDisplayName(friend),
            Note = string.Empty,
        };
    }

    private static void RemoveRequestsBetween(PersistedAppState state, Guid a, Guid b)
    {
        state.FriendRequests.RemoveAll(item =>
            (item.SenderAccountId == a && item.RecipientAccountId == b)
            || (item.SenderAccountId == b && item.RecipientAccountId == a));
    }

    private static bool MatchesFriendship(PersistedFriendship friendship, Guid a, Guid b)
    {
        return (friendship.AccountAId == a && friendship.AccountBId == b)
            || (friendship.AccountAId == b && friendship.AccountBId == a);
    }
}
