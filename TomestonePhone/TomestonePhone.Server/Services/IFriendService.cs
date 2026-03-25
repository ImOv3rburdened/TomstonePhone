using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface IFriendService
{
    Task<IReadOnlyList<FriendRequestRecord>> GetRequestsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<FriendRequestRecord> CreateRequestAsync(Guid senderAccountId, FriendRequestCreateRequest request, CancellationToken cancellationToken = default);

    Task<FriendRequestRecord?> RespondAsync(Guid accountId, RespondFriendRequest request, CancellationToken cancellationToken = default);

    Task<bool> RemoveFriendshipAsync(Guid accountId, RemoveFriendRequest request, CancellationToken cancellationToken = default);
}
