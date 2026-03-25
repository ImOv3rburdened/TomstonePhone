using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface IPhoneDirectoryService
{
    Task<IReadOnlyList<ContactRecord>> GetContactsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactRecord>> GetBlockedContactsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<ContactRecord> UpsertContactAsync(Guid ownerAccountId, ContactNoteUpdateRequest request, CancellationToken cancellationToken = default);

    Task<bool> BlockAccountAsync(Guid ownerAccountId, BlockAccountRequest request, CancellationToken cancellationToken = default);

    Task<bool> UnblockAccountAsync(Guid ownerAccountId, UnblockAccountRequest request, CancellationToken cancellationToken = default);
}
