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
                .Where(item => item.Id != accountId
                    && owner.ContactPreferences.ContainsKey(item.Id)
                    && !owner.BlockedAccountIds.Contains(item.Id)
                    && !item.BlockedAccountIds.Contains(accountId)
                    && !AccountLabelFormatter.IsUnavailable(item))
                .Select(item =>
                {
                    var preference = owner.ContactPreferences[item.Id];
                    var note = string.Equals(preference.Note, item.PhoneNumber, StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : preference.Note;
                    return new ContactRecord(item.Id, AccountLabelFormatter.GetContactDisplayName(item, preference.DisplayName), item.PhoneNumber, note);
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
                .Select(item => new ContactRecord(item.Id, AccountLabelFormatter.GetDisplayName(item), item.PhoneNumber, "Blocked"))
                .OrderBy(item => item.DisplayName)
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DirectoryPersonRecord>> SearchPeopleAsync(Guid accountId, string query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim();
        if (normalized.Length < 2)
        {
            return Task.FromResult<IReadOnlyList<DirectoryPersonRecord>>([]);
        }

        return this.repository.ReadAsync<IReadOnlyList<DirectoryPersonRecord>>(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == accountId);
            return state.Accounts
                .Where(item => item.Id != accountId
                    && !owner.BlockedAccountIds.Contains(item.Id)
                    && !item.BlockedAccountIds.Contains(accountId)
                    && !AccountLabelFormatter.IsUnavailable(item)
                    && (item.Username.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.PhoneNumber.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => AccountLabelFormatter.GetDisplayName(item), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PhoneNumber, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .Select(item => new DirectoryPersonRecord(item.Id, item.Username, AccountLabelFormatter.GetDisplayName(item), item.PhoneNumber))
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
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? AccountLabelFormatter.GetDisplayName(contact) : request.DisplayName,
                Note = request.Note ?? string.Empty,
            };

            return new ContactRecord(contact.Id, owner.ContactPreferences[contact.Id].DisplayName, contact.PhoneNumber, owner.ContactPreferences[contact.Id].Note);
        }, cancellationToken);
    }

    public Task<bool> RemoveContactAsync(Guid ownerAccountId, Guid contactAccountId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
            return owner.ContactPreferences.Remove(contactAccountId);
        }, cancellationToken);
    }

    public Task<bool> BlockAccountAsync(Guid ownerAccountId, BlockAccountRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
            var target = state.Accounts.SingleOrDefault(item => item.Id == request.TargetAccountId);
            if (target is null || target.Id == ownerAccountId || AccountLabelFormatter.IsUnavailable(target))
            {
                throw new InvalidOperationException("That account cannot be blocked.");
            }

            owner.BlockedAccountIds.Add(request.TargetAccountId);
            state.Friendships.RemoveAll(item =>
                (item.AccountAId == ownerAccountId && item.AccountBId == request.TargetAccountId)
                || (item.AccountAId == request.TargetAccountId && item.AccountBId == ownerAccountId));
            state.FriendRequests.RemoveAll(item =>
                (item.SenderAccountId == ownerAccountId && item.RecipientAccountId == request.TargetAccountId)
                || (item.SenderAccountId == request.TargetAccountId && item.RecipientAccountId == ownerAccountId));
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
