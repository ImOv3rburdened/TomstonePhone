using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TomestonePhone.Server.Hubs;
using TomestonePhone.Server.Services;
using TomestonePhone.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024;
});

builder.Services.Configure<CloudflareModerationOptions>(builder.Configuration.GetSection("CloudflareModeration"));
builder.Services.Configure<BootstrapOwnerOptions>(builder.Configuration.GetSection("BootstrapOwner"));
builder.Services.Configure<MariaDbOptions>(builder.Configuration.GetSection("MariaDb"));
builder.Services.AddSingleton<IPhoneRepository, MariaDbPhoneRepository>();
builder.Services.AddSingleton<IAccountService, AccountService>();
builder.Services.AddSingleton<IPhoneDirectoryService, PhoneDirectoryService>();
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<ICallService, CallService>();
builder.Services.AddSingleton<IFriendService, FriendService>();
builder.Services.AddSingleton<IReportService, ReportService>();
builder.Services.AddSingleton<ISupportTicketService, SupportTicketService>();
builder.Services.AddSingleton<ICloudflareModerationService, CloudflareModerationService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("TomestonePhone", policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true);
    });
});
builder.Services.AddSignalR();

var app = builder.Build();
await app.Services.GetRequiredService<IPhoneRepository>().InitializeAsync();
app.UseCors("TomestonePhone");
app.Use(async (context, next) =>
{
    var accounts = context.RequestServices.GetRequiredService<IAccountService>();
    var ip = RequestIpResolver.Resolve(context);
    if (!context.Request.Path.StartsWithSegments("/health")
        && await accounts.IsIpBannedAsync(ip))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "This IP address is banned." });
        return;
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTimeOffset.UtcNow }));

app.MapPost("/api/auth/register", async (HttpContext context, RegisterRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await accounts.RegisterAsync(request, RequestIpResolver.Resolve(context), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/auth/login", async (HttpContext context, LoginRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var response = await accounts.LoginAsync(request.Username, request.Password, RequestIpResolver.Resolve(context), cancellationToken);
    return response is null
        ? Results.Json(new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.Ok(response);
});

app.MapGet("/api/phone/me", async (HttpContext context, IAccountService accounts, IPhoneDirectoryService directory, IChatService chat, ICallService calls, IFriendService friends, IReportService reports, ISupportTicketService tickets, IPhoneRepository repository, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    var snapshot = new PhoneSnapshot(
        await accounts.GetProfileAsync(accountId.Value, cancellationToken),
        await repository.ReadAsync<IReadOnlyList<FriendshipRecord>>(state =>
        {
            return state.Friendships
                .Where(item => item.AccountAId == accountId.Value || item.AccountBId == accountId.Value)
                .Select(item =>
                {
                    var friendId = item.AccountAId == accountId.Value ? item.AccountBId : item.AccountAId;
                    var friend = state.Accounts.Single(account => account.Id == friendId);
                    return new FriendshipRecord(item.Id, friendId, friend.DisplayName, friend.PhoneNumber, item.CreatedAtUtc);
                })
                .ToList();
        }, cancellationToken),
        await directory.GetContactsAsync(accountId.Value, cancellationToken),
        await directory.GetBlockedContactsAsync(accountId.Value, cancellationToken),
        await chat.GetConversationsAsync(accountId.Value, cancellationToken),
        await calls.GetRecentCallsAsync(accountId.Value, cancellationToken),
        await friends.GetRequestsAsync(accountId.Value, cancellationToken),
        await reports.GetVisibleReportsAsync(accountId.Value, cancellationToken),
        await repository.ReadAsync<IReadOnlyList<AuditLogRecord>>(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId.Value);
            var isStaff = account.Role is nameof(AccountRole.Owner) or nameof(AccountRole.Admin) or nameof(AccountRole.Moderator);
            return state.AuditLogs
                .Where(item => isStaff || item.ActorAccountId == accountId.Value)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => new AuditLogRecord(item.Id, item.ActorAccountId, item.ActorDisplayName, item.EventType, item.Summary, item.CreatedAtUtc))
                .ToList();
        }, cancellationToken),
        await tickets.GetTicketsAsync(accountId.Value, cancellationToken));

    return Results.Ok(snapshot);
});

app.MapPut("/api/contacts", async (HttpContext context, ContactNoteUpdateRequest request, IAccountService accounts, IPhoneDirectoryService directory, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    return Results.Ok(await directory.UpsertContactAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/contacts/block", async (HttpContext context, BlockAccountRequest request, IAccountService accounts, IPhoneDirectoryService directory, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { success = await directory.BlockAccountAsync(accountId.Value, request, cancellationToken) });
});

app.MapPost("/api/contacts/unblock", async (HttpContext context, UnblockAccountRequest request, IAccountService accounts, IPhoneDirectoryService directory, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { success = await directory.UnblockAccountAsync(accountId.Value, request, cancellationToken) });
});

app.MapPost("/api/account/privacy", async (HttpContext context, AcceptPrivacyPolicyRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await accounts.AcceptPrivacyPolicyAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/account/game-identity", async (HttpContext context, UpdateGameIdentityRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await accounts.UpdateGameIdentityAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/account/password", async (HttpContext context, PasswordResetSelfRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { success = await accounts.ChangePasswordAsync(accountId.Value, request, cancellationToken) });
});

app.MapPost("/api/account/notifications", async (HttpContext context, UpdateNotificationSettingsRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await accounts.UpdateNotificationSettingsAsync(accountId.Value, request, cancellationToken));
});

app.MapGet("/api/conversations/{conversationId:guid}/messages", async (HttpContext context, Guid conversationId, IAccountService accounts, IChatService chat, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await chat.GetMessagesAsync(accountId.Value, conversationId, cancellationToken));
});

app.MapGet("/api/conversations/{conversationId:guid}", async (HttpContext context, Guid conversationId, IAccountService accounts, IChatService chat, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await chat.GetConversationDetailAsync(accountId.Value, conversationId, cancellationToken));
});

app.MapPost("/api/conversations", async (HttpContext context, CreateConversationRequest request, IAccountService accounts, IChatService chat, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    return Results.Ok(await chat.CreateConversationAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/conversations/direct", async (HttpContext context, StartDirectConversationRequest request, IAccountService accounts, IChatService chat, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    return Results.Ok(await chat.StartDirectConversationAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/messages", async (HttpContext context, SendMessageRequest request, IAccountService accounts, IChatService chat, IHubContext<PhoneHub> hub, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    try
    {
        var message = await chat.SendMessageAsync(accountId.Value, request, cancellationToken);
        await hub.Clients.Group(request.ConversationId.ToString()).SendAsync("message", message, cancellationToken);
        return Results.Ok(message);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/conversations/moderate", async (HttpContext context, ConversationModerationRequest request, IAccountService accounts, IChatService chat, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    var detail = await chat.ModerateConversationAsync(accountId.Value, request, cancellationToken);
    return detail is null ? Results.BadRequest() : Results.Ok(detail);
});

app.MapPost("/api/calls/start", async (HttpContext context, StartCallRequest request, IAccountService accounts, ICallService calls, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    return Results.Ok(await calls.StartCallAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/calls/complete", async (HttpContext context, CompleteCallRequest request, IAccountService accounts, ICallService calls, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    var result = await calls.CompleteCallAsync(accountId.Value, request, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/friends", async (HttpContext context, FriendRequestCreateRequest request, IAccountService accounts, IFriendService friends, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    try
    {
        return Results.Ok(await friends.CreateRequestAsync(accountId.Value, request, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/friends/respond", async (HttpContext context, RespondFriendRequest request, IAccountService accounts, IFriendService friends, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    var response = await friends.RespondAsync(accountId.Value, request, cancellationToken);
    return response is null ? Results.NotFound() : Results.Ok(response);
});

app.MapPost("/api/friends/remove", async (HttpContext context, RemoveFriendRequest request, IAccountService accounts, IFriendService friends, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    if (!await EnsureInteractiveAccessAsync(accountId.Value, accounts, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    return Results.Ok(new { success = await friends.RemoveFriendshipAsync(accountId.Value, request, cancellationToken) });
});

app.MapPost("/api/reports", async (HttpContext context, CreateReportRequest request, IAccountService accounts, IReportService reports, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await reports.CreateReportAsync(accountId.Value, request, cancellationToken));
});

app.MapPost("/api/reports/reply", async (HttpContext context, ReportReplyRequest request, IAccountService accounts, IReportService reports, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    var result = await reports.ReplyToReportAsync(accountId.Value, request, cancellationToken);
    return result is null ? Results.BadRequest() : Results.Ok(result);
});

app.MapPost("/api/moderation/cloudflare/csam-alert", async (
    HttpContext context,
    CloudflareCsamAlert alert,
    IOptions<CloudflareModerationOptions> options,
    ICloudflareModerationService moderation,
    CancellationToken cancellationToken) =>
{
    var configuredSecret = options.Value.AlertSharedSecret;
    var headerSecret = context.Request.Headers["X-Tomestone-Moderation-Secret"].ToString();

    if (string.IsNullOrWhiteSpace(configuredSecret) || !string.Equals(configuredSecret, headerSecret, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    await moderation.HandleCsamAlertAsync(alert, cancellationToken);
    return Results.Ok(new { status = "accepted" });
});

app.MapGet("/api/support/tickets", async (HttpContext context, IAccountService accounts, ISupportTicketService tickets, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await tickets.GetTicketsAsync(accountId.Value, cancellationToken));
});

app.MapPost("/api/support/tickets", async (HttpContext context, CreateSupportTicketRequest request, IAccountService accounts, ISupportTicketService tickets, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await tickets.CreateTicketAsync(accountId.Value, request, cancellationToken));
});

app.MapGet("/api/admin/dashboard", async (HttpContext context, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Ok(await accounts.GetAdminDashboardAsync(accountId.Value, cancellationToken));
    }
    catch (InvalidOperationException)
    {
        return Results.Unauthorized();
    }
});

app.MapPost("/api/admin/reset-password", async (HttpContext context, AdminPasswordResetRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
{
    var accountId = await ResolveAccountIdAsync(context, accounts, cancellationToken);
    if (accountId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { success = await accounts.ResetPasswordAsOwnerAsync(accountId.Value, request, cancellationToken) });
});

app.MapHub<PhoneHub>("/hubs/phone");

app.Run();

static async Task<Guid?> ResolveAccountIdAsync(HttpContext context, IAccountService accounts, CancellationToken cancellationToken)
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = header["Bearer ".Length..].Trim();
    return await accounts.AuthenticateAsync(token, cancellationToken);
}

static async Task<bool> EnsureInteractiveAccessAsync(Guid accountId, IAccountService accounts, CancellationToken cancellationToken)
{
    var profile = await accounts.GetProfileAsync(accountId, cancellationToken);
    return profile.Status == AccountStatus.Active;
}


