using System.Security.Cryptography;
using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class AccountService : IAccountService
{
    private readonly IPhoneRepository repository;

    public AccountService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<Guid?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state =>
        {
            var session = state.Sessions.SingleOrDefault(item => item.Token == token && item.ExpiresAtUtc > DateTimeOffset.UtcNow);
            return session?.AccountId;
        }, cancellationToken);
    }

    public Task<PhoneProfile> GetProfileAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state => MapProfile(state.Accounts.Single(item => item.Id == accountId)), cancellationToken);
    }

    public Task<bool> IsIpBannedAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state => state.IpBans.Any(item => item.IpAddress == ipAddress), cancellationToken);
    }

    public Task<LoginResponse?> LoginAsync(string username, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            if (state.IpBans.Any(item => item.IpAddress == ipAddress))
            {
                return null;
            }

            var account = state.Accounts.SingleOrDefault(item => item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (account is null || !PasswordHasher.Verify(password, account.PasswordSalt, account.PasswordHash))
            {
                return null;
            }

            if (account.Status is nameof(AccountStatus.Banned) or nameof(AccountStatus.Suspended))
            {
                return null;
            }

            account.KnownIpAddresses.Add(ipAddress);

            var session = new PersistedSession
            {
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                AccountId = account.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14),
            };

            state.Sessions.RemoveAll(item => item.AccountId == account.Id);
            state.Sessions.Add(session);
            return new LoginResponse(account.Id, account.Username, account.PhoneNumber, session.Token, session.ExpiresAtUtc);
        }, cancellationToken);
    }

    public Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            if (state.IpBans.Any(item => item.IpAddress == ipAddress))
            {
                throw new InvalidOperationException("This IP address is banned.");
            }

            if (!request.AcceptedLegalTerms || !request.AcceptedPrivacyPolicy)
            {
                throw new InvalidOperationException("Terms and privacy policy must be accepted before registration.");
            }

            if (state.Accounts.Any(item => item.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            state.NextPhoneNumber++;

            var account = new PersistedAccount
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                DisplayName = request.Username,
                PasswordSalt = salt,
                PasswordHash = PasswordHasher.Hash(request.Password, salt),
                PhoneNumber = state.NextPhoneNumber.ToString("0000000000"),
                Role = nameof(AccountRole.User),
                Status = nameof(AccountStatus.Active),
                AcceptedLegalTermsVersion = request.LegalTermsVersion,
                AcceptedLegalTermsAtUtc = request.AcceptedAtUtc,
                AcceptedPrivacyPolicyVersion = request.PrivacyPolicyVersion,
                AcceptedPrivacyPolicyAtUtc = request.AcceptedPrivacyAtUtc,
                KnownIpAddresses = [ipAddress],
            };

            state.Accounts.Add(account);

            var session = new PersistedSession
            {
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                AccountId = account.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14),
            };

            state.Sessions.Add(session);
            return new RegisterResponse(account.Id, account.Username, account.PhoneNumber, session.Token);
        }, cancellationToken);
    }

    public Task<PhoneProfile> UpdateNotificationSettingsAsync(Guid accountId, UpdateNotificationSettingsRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId);
            account.NotificationsMuted = request.NotificationsMuted;
            return MapProfile(account);
        }, cancellationToken);
    }

    public Task<bool> ChangePasswordAsync(Guid accountId, PasswordResetSelfRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            if (request.NewPassword != request.ConfirmPassword)
            {
                return false;
            }

            var account = state.Accounts.Single(item => item.Id == accountId);
            if (!PasswordHasher.Verify(request.OldPassword, account.PasswordSalt, account.PasswordHash))
            {
                return false;
            }

            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            account.PasswordSalt = salt;
            account.PasswordHash = PasswordHasher.Hash(request.NewPassword, salt);
            return true;
        }, cancellationToken);
    }

    public Task<PhoneProfile> AcceptPrivacyPolicyAsync(Guid accountId, AcceptPrivacyPolicyRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId);
            account.AcceptedPrivacyPolicyVersion = request.PrivacyPolicyVersion;
            account.AcceptedPrivacyPolicyAtUtc = request.AcceptedAtUtc;
            return MapProfile(account);
        }, cancellationToken);
    }

    public Task<PhoneProfile> UpdateGameIdentityAsync(Guid accountId, UpdateGameIdentityRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId);
            account.LastKnownGameIdentity = new PersistedGameIdentity
            {
                CharacterName = request.CharacterName,
                WorldName = request.WorldName,
                FullHandle = $"{request.CharacterName}@{request.WorldName}",
            };
            return MapProfile(account);
        }, cancellationToken);
    }

    public Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == accountId);
            SystemConversationCoordinator.EnsureStaffConversation(state);
            if (actor.Role != nameof(AccountRole.Owner) && actor.Role != nameof(AccountRole.Admin) && actor.Role != nameof(AccountRole.Moderator))
            {
                throw new InvalidOperationException("Not authorized.");
            }

            return new AdminDashboardSnapshot(
                state.Accounts.Select(MapAdminSummary).OrderBy(item => item.Username).ToList(),
                state.Reports.OrderByDescending(item => item.CreatedAtUtc).Select(item => new ReportRecord(
                    item.Id,
                    Enum.TryParse<ReportCategory>(item.Category, out var category) ? category : ReportCategory.Message,
                    item.ReporterAccountId,
                    state.Accounts.SingleOrDefault(account => account.Id == item.ReporterAccountId)?.DisplayName ?? "Unknown",
                    item.TargetAccountId,
                    item.TargetConversationId,
                    item.TargetMessageId,
                    item.TargetImageId,
                    item.Reason,
                    item.SuspectedCsam,
                    Enum.TryParse<ReportStatus>(item.Status, out var status) ? status : ReportStatus.Open,
                    item.CreatedAtUtc)).ToList(),
                state.AuditLogs.OrderByDescending(item => item.CreatedAtUtc).Select(item => new AuditLogRecord(item.Id, item.ActorAccountId, item.ActorDisplayName, item.EventType, item.Summary, item.CreatedAtUtc)).ToList(),
                state.SupportTickets.OrderByDescending(item => item.CreatedAtUtc).Select(item =>
                {
                    var owner = state.Accounts.Single(account => account.Id == item.AccountId);
                    return new SupportTicketRecord(item.Id, item.ConversationId, item.AccountId, owner.DisplayName, item.Subject, item.Body, Enum.TryParse<SupportTicketStatus>(item.Status, out var ticketStatus) ? ticketStatus : SupportTicketStatus.Open, item.CreatedAtUtc, item.ClosedAtUtc, item.ClosedByAccountId, item.IsModerationCase);
                }).ToList());
        }, cancellationToken);
    }

    public Task<bool> ResetPasswordAsOwnerAsync(Guid actorAccountId, AdminPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            if (actor.Role != nameof(AccountRole.Owner))
            {
                return false;
            }

            var account = state.Accounts.SingleOrDefault(item => item.Id == request.AccountId);
            if (account is null)
            {
                return false;
            }

            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            account.PasswordSalt = salt;
            account.PasswordHash = PasswordHasher.Hash(request.NewPassword, salt);
            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = actor.Id,
                ActorDisplayName = actor.DisplayName,
                EventType = "OwnerPasswordReset",
                Summary = $"Owner reset password for {account.Username}.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            return true;
        }, cancellationToken);
    }


    public Task<bool> UpdateAccountRoleAsync(Guid actorAccountId, UpdateAccountRoleRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            if (actor.Role != nameof(AccountRole.Owner) || request.AccountId == actorAccountId)
            {
                return false;
            }

            var account = state.Accounts.SingleOrDefault(item => item.Id == request.AccountId);
            if (account is null || account.Role == nameof(AccountRole.Owner))
            {
                return false;
            }

            account.Role = request.Role.ToString();
            SystemConversationCoordinator.EnsureStaffConversation(state);
            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = actor.Id,
                ActorDisplayName = actor.DisplayName,
                EventType = "AccountRoleChanged",
                Summary = $"Owner changed role for {account.Username} to {account.Role}.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            return true;
        }, cancellationToken);
    }
    private static AdminAccountSummary MapAdminSummary(PersistedAccount account)
    {
        return new AdminAccountSummary(
            account.Id,
            account.Username,
            account.DisplayName,
            account.PhoneNumber,
            Enum.TryParse<AccountRole>(account.Role, out var role) ? role : AccountRole.User,
            Enum.TryParse<AccountStatus>(account.Status, out var status) ? status : AccountStatus.Active,
            account.KnownIpAddresses.OrderBy(item => item).ToList());
    }

    private static PhoneProfile MapProfile(PersistedAccount account)
    {
        var role = Enum.TryParse<AccountRole>(account.Role, out var parsedRole) ? parsedRole : AccountRole.User;
        var status = Enum.TryParse<AccountStatus>(account.Status, out var parsedStatus) ? parsedStatus : AccountStatus.Active;

        return new PhoneProfile(
            account.Id,
            account.Username,
            account.DisplayName,
            account.PhoneNumber,
            role,
            status,
            account.NotificationsMuted,
            account.AcceptedLegalTermsVersion,
            account.AcceptedPrivacyPolicyVersion,
            account.LastKnownGameIdentity is null
                ? null
                : new GameIdentityRecord(account.LastKnownGameIdentity.CharacterName, account.LastKnownGameIdentity.WorldName, account.LastKnownGameIdentity.FullHandle),
            account.Wallpaper is null
                ? null
                : new WallpaperLayout(account.Wallpaper.RelativePath, account.Wallpaper.Zoom, account.Wallpaper.OffsetX, account.Wallpaper.OffsetY, account.Wallpaper.ViewportWidth, account.Wallpaper.ViewportHeight, account.Wallpaper.UpdatedAtUtc),
            account.Avatar is null
                ? null
                : new UserAvatarLayout(account.Avatar.RelativePath, account.Avatar.Zoom, account.Avatar.OffsetX, account.Avatar.OffsetY, account.Avatar.ViewportSize, account.Avatar.UpdatedAtUtc));
    }
}

