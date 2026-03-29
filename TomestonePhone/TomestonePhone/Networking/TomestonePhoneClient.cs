using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dalamud.Plugin.Services;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Networking;

public sealed class TomestonePhoneClient : IDisposable
{
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
        if (!response.IsSuccessStatusCode)
        {
            var payload = await this.TryReadErrorPayloadAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(payload?.Error ?? "Invalid username or password.");
            }

            throw new InvalidOperationException(payload?.Error ?? $"Login failed ({(int)response.StatusCode}).");
        }

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
        if (!response.IsSuccessStatusCode)
        {
            var payload = await this.TryReadErrorPayloadAsync(response, cancellationToken);
            throw new InvalidOperationException(payload?.Error ?? $"Registration failed ({(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Registration returned no payload.");
    }

    public async Task<PhoneSnapshot> GetSnapshotAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/phone/me", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneSnapshot>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Snapshot returned no payload.");
    }

    public async Task<PhoneProfile> UpdateGameIdentityAsync(string token, UpdateGameIdentityRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/game-identity", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Game identity update returned no payload.");
    }

    public async Task<ConversationDetail> GetConversationDetailAsync(string token, Guid conversationId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync($"/api/conversations/{conversationId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationDetail>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Conversation detail returned no payload.");
    }

    public async Task<ConversationMessagePage> GetConversationMessagesAsync(string token, Guid conversationId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync($"/api/conversations/{conversationId}/messages", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationMessagePage>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Conversation page returned no payload.");
    }

    public async Task<ConversationSummary> StartDirectConversationAsync(string token, StartDirectConversationRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/conversations/direct", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationSummary>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Direct conversation returned no payload.");
    }

    public async Task<CallSummary> StartCallAsync(string token, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/calls/start", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CallSummary>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Call start returned no payload.");
    }
    public async Task<bool> BlockAccountAsync(string token, Guid targetAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/contacts/block", new BlockAccountRequest(targetAccountId), cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<bool> UnblockAccountAsync(string token, Guid targetAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/contacts/unblock", new UnblockAccountRequest(targetAccountId), cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<ContactRecord> AddContactAsync(string token, Guid targetAccountId, string displayName, string note, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PutAsJsonAsync("/api/contacts", new ContactNoteUpdateRequest(targetAccountId, displayName, note), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContactRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Add contact returned no payload.");
    }

    public async Task<ChatMessageRecord> SendMessageAsync(string token, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/messages", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await this.TryReadErrorPayloadAsync(response, cancellationToken);
            throw new InvalidOperationException(payload?.Error ?? "Message delivery failed.");
        }

        return await response.Content.ReadFromJsonAsync<ChatMessageRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Message send returned no payload.");
    }

    public async Task<FriendRequestRecord> CreateFriendRequestAsync(string token, FriendRequestCreateRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FriendRequestRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Friend request returned no payload.");
    }

    public async Task<FriendRequestRecord?> RespondToFriendRequestAsync(string token, RespondFriendRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends/respond", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FriendRequestRecord>(cancellationToken: cancellationToken);
    }

    public async Task<bool> RemoveFriendAsync(string token, Guid friendAccountId, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/friends/remove", new RemoveFriendRequest(friendAccountId), cancellationToken);
        response.EnsureSuccessStatusCode();
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
            return null;
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
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ReportReplyResult>(cancellationToken: cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(string token, PasswordResetSelfRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/password", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public async Task<PhoneProfile> UpdateNotificationSettingsAsync(string token, bool muted, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/notifications", new UpdateNotificationSettingsRequest(muted), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Notification settings returned no payload.");
    }

    public async Task<PhoneProfile> AcceptPrivacyPolicyAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/account/privacy", new AcceptPrivacyPolicyRequest(PrivacyPolicy.Version, DateTimeOffset.UtcNow), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Privacy acceptance returned no payload.");
    }

    public async Task<IReadOnlyList<SupportTicketRecord>> GetSupportTicketsAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/support/tickets", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SupportTicketRecord>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<SupportTicketRecord> CreateSupportTicketAsync(string token, CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/support/tickets", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupportTicketRecord>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Support ticket returned no payload.");
    }

    public async Task<AdminDashboardSnapshot> GetAdminDashboardAsync(string token, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.GetAsync("/api/admin/dashboard", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AdminDashboardSnapshot>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Admin dashboard returned no payload.");
    }

    public async Task<bool> ResetPasswordAsOwnerAsync(string token, AdminPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        this.ApplyBaseAddress();
        this.SetAuth(token);
        var response = await this.httpClient.PostAsJsonAsync("/api/admin/reset-password", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
        return payload?.Success ?? false;
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
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
    }

    private void SetAuth(string token)
    {
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class OperationResult
    {
        public bool Success { get; set; }
    }

    private sealed class ErrorPayload
    {
        public string? Error { get; set; }
    }
}

