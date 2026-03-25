using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class PhoneDirectoryService : IPhoneDirectoryService
{
    private readonly IPhoneRepository repository;

    public PhoneDirectoryService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<IReadOnlyList<ContactRecord>> GetContactsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<ContactRecord>>(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == accountId);
            return state.Accounts
                .Where(item => item.Id != accountId && owner.ContactPreferences.ContainsKey(item.Id))
                .Select(item =>
                {
                    var preference = owner.ContactPreferences[item.Id];
                    return new ContactRecord(item.Id, preference.DisplayName, item.PhoneNumber, preference.Note);
                })
                .OrderBy(item => item.DisplayName)
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ContactRecord>> GetBlockedContactsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<ContactRecord>>(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == accountId);
            return state.Accounts
                .Where(item => owner.BlockedAccountIds.Contains(item.Id))
                .Select(item => new ContactRecord(item.Id, item.DisplayName, item.PhoneNumber, "Blocked"))
                .OrderBy(item => item.DisplayName)
                .ToList();
        }, cancellationToken);
    }

    public Task<ContactRecord> UpsertContactAsync(Guid ownerAccountId, ContactNoteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
            var contact = state.Accounts.Single(item => item.Id == request.ContactAccountId);
            owner.ContactPreferences[contact.Id] = new PersistedContactPreference
            {
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? contact.DisplayName : request.DisplayName,
                Note = request.Note ?? string.Empty,
            };

            return new ContactRecord(contact.Id, owner.ContactPreferences[contact.Id].DisplayName, contact.PhoneNumber, owner.ContactPreferences[contact.Id].Note);
        }, cancellationToken);
    }

    public Task<bool> BlockAccountAsync(Guid ownerAccountId, BlockAccountRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
            owner.BlockedAccountIds.Add(request.TargetAccountId);
            return true;
        }, cancellationToken);
    }

    public Task<bool> UnblockAccountAsync(Guid ownerAccountId, UnblockAccountRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
            return owner.BlockedAccountIds.Remove(request.TargetAccountId);
        }, cancellationToken);
    }
}
