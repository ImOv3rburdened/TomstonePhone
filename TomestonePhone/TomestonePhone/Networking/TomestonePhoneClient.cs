using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dalamud.Plugin.Services;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Networking;

public sealed class TomestonePhoneClient : IDisposable
{
    private const string ClientVersionHeaderName = "X-TomestonePhone-Version";
    private static readonly string CurrentClientVersion = typeof(TomestonePhoneClient).Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0";
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly HttpClient httpClient = new();

    public TomestonePhoneClient(Configuration configuration, IPluginLog log)
    {
        this.configuration = configuration;
        this.log = log;
        this.httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        var response = await this.httpClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var payload = await this.TryReadErrorPayloadAsync(response, cancellationToken);
            throw new InvalidOperationException(payload?.Error ?? "Invalid username or password.");
        }

        await this.EnsureSuccessAsync(response, "Login", cancellationToken);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Login returned no payload.");
    }

    public async Task<RegisterResponse> RegisterAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        var request = new RegisterRequest(
            username,
            password,
            true,
            LegalTerms.Version,
            DateTimeOffset.UtcNow,
            true,
            PrivacyPolicy.Version,
            DateTimeOffset.UtcNow);

        var response = await this.httpClient.PostAsJsonAsync("/api/auth/register", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Registration", cancellationToken);
        return await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Registration returned no payload.");
    }

    public async Task<ClientVersionPolicyResult> GetVersionPolicyAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        var response = await this.httpClient.GetAsync("/api/client/version-policy", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClientVersionPolicyResult>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Version policy returned no payload.");
    }

    public async Task<PhoneSnapshot> GetSnapshotAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/phone/me", cancellationToken);
        await this.EnsureSuccessAsync(response, "Account snapshot", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PhoneSnapshot>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Snapshot returned no payload.");
    }

    public async Task<PhoneProfile> UpdateGameIdentityAsync(string token, UpdateGameIdentityRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/game-identity", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Game identity update", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Game identity update returned no payload.");
    }

    public async Task<ConversationDetail> GetConversationDetailAsync(string token, Guid conversationId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync($"/api/conversations/{conversationId}", cancellationToken);
        await this.EnsureSuccessAsync(response, "Conversation detail", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConversationDetail>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Conversation detail returned no payload.");
    }

    public async Task<ConversationMessagePage> GetConversationMessagesAsync(string token, Guid conversationId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync($"/api/conversations/{conversationId}/messages", cancellationToken);
        await this.EnsureSuccessAsync(response, "Conversation messages", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConversationMessagePage>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Conversation page returned no payload.");
    }

    public async Task<ConversationSummary> StartDirectConversationAsync(string token, StartDirectConversationRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/conversations/direct", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Direct conversation", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConversationSummary>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Direct conversation returned no payload.");
    }


    public async Task<ConversationSummary> CreateConversationAsync(string token, CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/conversations", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Conversation creation", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConversationSummary>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Conversation creation returned no payload.");
    }

    public async Task<CallSummary> StartCallAsync(string token, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/calls/start", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Call start", cancellationToken);
        return await response.Content.ReadFromJsonAsync<CallSummary>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Call start returned no payload.");
    }

    public async Task<IReadOnlyList<ActiveCallSessionRecord>> GetActiveCallsAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/calls/active", cancellationToken);
        await this.EnsureSuccessAsync(response, "Active calls", cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ActiveCallSessionRecord>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<ActiveCallSessionRecord> StartOrJoinActiveCallAsync(string token, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/calls/session/start", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Active call session", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ActiveCallSessionRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Active call session returned no payload.");
    }

    public async Task<CallSummary?> EndActiveCallAsync(string token, Guid sessionId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/calls/session/end", new EndActiveCallRequest(sessionId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "End active call", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<CallSummary>(cancellationToken: cancellationToken);
    }

    public async Task<int> AcknowledgeMissedCallsAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsync("/api/calls/missed/acknowledge", null, cancellationToken);
        await this.EnsureSuccessAsync(response, "Acknowledge missed calls", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Count ?? 0;
    }
    public async Task<bool> BlockAccountAsync(string token, Guid targetAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/contacts/block", new BlockAccountRequest(targetAccountId), cancellationToken);
        await this.EnsureSuccessAsync(response, "Block account", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<bool> UnblockAccountAsync(string token, Guid targetAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/contacts/unblock", new UnblockAccountRequest(targetAccountId), cancellationToken);
        await this.EnsureSuccessAsync(response, "Unblock account", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<ContactRecord> AddContactAsync(string token, Guid targetAccountId, string displayName, string note, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PutAsJsonAsync("/api/contacts", new ContactNoteUpdateRequest(targetAccountId, displayName, note), cancellationToken);
        await this.EnsureSuccessAsync(response, "Add contact", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ContactRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Add contact returned no payload.");
    }

    public async Task<ChatMessageRecord> SendMessageAsync(string token, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/messages", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Message delivery", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<ChatMessageRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Message send returned no payload.");
    }

    public async Task<FriendRequestRecord> CreateFriendRequestAsync(string token, FriendRequestCreateRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Friend request", cancellationToken);
        return await response.Content.ReadFromJsonAsync<FriendRequestRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Friend request returned no payload.");
    }

    public async Task<FriendRequestRecord?> RespondToFriendRequestAsync(string token, RespondFriendRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends/respond", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Friend request response", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<FriendRequestRecord>(cancellationToken: cancellationToken);
    }

    public async Task<bool> RemoveFriendAsync(string token, Guid friendAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends/remove", new RemoveFriendRequest(friendAccountId), cancellationToken);
        await this.EnsureSuccessAsync(response, "Remove friend", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<ConversationDetail?> ModerateConversationAsync(string token, ConversationModerationRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/conversations/moderate", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Conversation moderation", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<ConversationDetail>(cancellationToken: cancellationToken);
    }

    public async Task<ReportReplyResult?> ReplyToReportAsync(string token, ReportReplyRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/reports/reply", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Report reply", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<ReportReplyResult>(cancellationToken: cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(string token, PasswordResetSelfRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/password", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Password change", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<bool> DeleteAccountAsync(string token, DeleteAccountRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/delete", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Delete account", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<PhoneProfile> UpdateNotificationSettingsAsync(string token, bool muted, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/notifications", new UpdateNotificationSettingsRequest(muted), cancellationToken);
        await this.EnsureSuccessAsync(response, "Notification settings", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Notification settings returned no payload.");
    }

    public async Task<PhoneProfile> UpdatePresenceStatusAsync(string token, PhonePresenceStatus presenceStatus, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/presence", new UpdatePresenceStatusRequest(presenceStatus), cancellationToken);
        await this.EnsureSuccessAsync(response, "Presence status", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Presence status returned no payload.");
    }

    public async Task<PhoneProfile> AcceptPrivacyPolicyAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/privacy", new AcceptPrivacyPolicyRequest(PrivacyPolicy.Version, DateTimeOffset.UtcNow), cancellationToken);
        await this.EnsureSuccessAsync(response, "Privacy acceptance", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Privacy acceptance returned no payload.");
    }

    public async Task<IReadOnlyList<SupportTicketRecord>> GetSupportTicketsAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/support/tickets", cancellationToken);
        await this.EnsureSuccessAsync(response, "Support tickets", cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SupportTicketRecord>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<SupportTicketRecord> CreateSupportTicketAsync(string token, CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/support/tickets", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Support ticket", cancellationToken);
        return await response.Content.ReadFromJsonAsync<SupportTicketRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Support ticket returned no payload.");
    }

    public async Task<SupportTicketRecord?> CloseSupportTicketAsync(string token, Guid ticketId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsync($"/api/support/tickets/{ticketId}/close", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Close support ticket", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<SupportTicketRecord>(cancellationToken: cancellationToken);
    }

    public async Task<SupportTicketRecord?> AddSupportTicketParticipantAsync(string token, Guid ticketId, Guid accountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/support/tickets/participants", new AddSupportTicketParticipantRequest(ticketId, accountId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Add support ticket participant", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<SupportTicketRecord>(cancellationToken: cancellationToken);
    }

    public async Task<AdminDashboardSnapshot> GetAdminDashboardAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/admin/dashboard", cancellationToken);
        await this.EnsureSuccessAsync(response, "Admin dashboard", cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminDashboardSnapshot>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Admin dashboard returned no payload.");
    }

    public async Task<bool> UpdateAccountRoleAsync(string token, UpdateAccountRoleRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/admin/account-role", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Update account role", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<bool> ResetPasswordAsOwnerAsync(string token, AdminPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/admin/reset-password", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Owner password reset", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }


    public async Task<bool> UpdateAccountStatusAsync(string token, UpdateAccountStatusRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/admin/account-status", request, cancellationToken);
        await this.EnsureSuccessAsync(response, "Update account status", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<ServerAnnouncementRecord?> UpsertServerAnnouncementAsync(string token, UpsertServerAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/admin/announcement", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await this.EnsureSuccessAsync(response, "Server announcement", cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<ServerAnnouncementRecord>(cancellationToken: cancellationToken);
    }

    public async Task<bool> ClearServerAnnouncementAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsync("/api/admin/announcement/clear", null, cancellationToken);
        await this.EnsureSuccessAsync(response, "Clear server announcement", cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }
    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operationName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await this.TryReadErrorPayloadAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new ClientUpgradeRequiredException(
                payload?.MinimumVersion ?? string.Empty,
                payload?.Error ?? payload?.UpdateMessage ?? "Please update TomestonePhone to the latest version before using the app.");
        }

        var detail = payload?.Error;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = $"HTTP {(int)response.StatusCode}";
        }

        throw new InvalidOperationException($"{operationName} failed: {detail}");
    }

    private async Task<ErrorPayload?> TryReadErrorPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorPayload>(cancellationToken: cancellationToken);
        }
        catch
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : new ErrorPayload { Error = text.Trim() };
        }
    }
    private void ApplyBaseAddress()
    {
        var target = this.configuration.ServerBaseUrl.TrimEnd('/');
        if (this.httpClient.BaseAddress?.ToString().TrimEnd('/') != target)
        {
            this.httpClient.BaseAddress = new Uri(target, UriKind.Absolute);
        }

        this.httpClient.DefaultRequestHeaders.Remove(ClientVersionHeaderName);
        this.httpClient.DefaultRequestHeaders.Add(ClientVersionHeaderName, CurrentClientVersion);
    }

    private void SetAuth(string token)
    {
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class OperationResult
    {
        public bool Success { get; set; }

        public int Count { get; set; }
    }

    private sealed class ErrorPayload
    {
        public string? Error { get; set; }

        public string? MinimumVersion { get; set; }

        public string? RecommendedVersion { get; set; }

        public string? UpdateMessage { get; set; }

        public string? RecommendedMessage { get; set; }
    }
}

public sealed record ClientVersionPolicyResult(string MinimumVersion, string RecommendedVersion, string UpdateMessage, string RecommendedMessage);

public sealed class ClientUpgradeRequiredException(string minimumVersion, string updateMessage) : InvalidOperationException(updateMessage)
{
    public string MinimumVersion { get; } = minimumVersion;

    public string UpdateMessage { get; } = updateMessage;
}




