using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface IAccountService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress, CancellationToken cancellationToken = default);

    Task<LoginResponse?> LoginAsync(string username, string password, string ipAddress, CancellationToken cancellationToken = default);

    Task<PhoneProfile> GetProfileAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Guid?> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    Task<bool> IsIpBannedAsync(string ipAddress, CancellationToken cancellationToken = default);

    Task<PhoneProfile> UpdateNotificationSettingsAsync(Guid accountId, UpdateNotificationSettingsRequest request, CancellationToken cancellationToken = default);

    Task<bool> ChangePasswordAsync(Guid accountId, PasswordResetSelfRequest request, CancellationToken cancellationToken = default);

    Task<PhoneProfile> AcceptPrivacyPolicyAsync(Guid accountId, AcceptPrivacyPolicyRequest request, CancellationToken cancellationToken = default);

    Task<PhoneProfile> UpdateGameIdentityAsync(Guid accountId, UpdateGameIdentityRequest request, CancellationToken cancellationToken = default);

    Task<AdminDashboardSnapshot> GetAdminDashboardAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<bool> ResetPasswordAsOwnerAsync(Guid actorAccountId, AdminPasswordResetRequest request, CancellationToken cancellationToken = default);

    Task<bool> UpdateAccountRoleAsync(Guid actorAccountId, UpdateAccountRoleRequest request, CancellationToken cancellationToken = default);

    Task<bool> UpdateAccountStatusAsync(Guid actorAccountId, UpdateAccountStatusRequest request, CancellationToken cancellationToken = default);

    Task<ServerAnnouncementRecord?> UpsertServerAnnouncementAsync(Guid actorAccountId, UpsertServerAnnouncementRequest request, CancellationToken cancellationToken = default);

    Task<bool> ClearServerAnnouncementAsync(Guid actorAccountId, CancellationToken cancellationToken = default);
}