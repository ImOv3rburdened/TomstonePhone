using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using TomestonePhone.Networking;
using TomestonePhone.Shared.Models;
using TomestonePhone.Voice;

namespace TomestonePhone.UI;

public sealed class PhoneWindow : Window
{
    private enum MessageFolder
    {
        Regular,
        Tickets,
        Staff,
    }
    private const float PhoneAspectRatio = 390f / 844f;
    private const float DefaultWindowWidth = 440f;
    private const float MinimumWindowScale = 0.7f;
    private const float MaximumWindowScale = 1.35f;
    private const float DefaultWindowHeight = 952f;
    private const string GiphyCreateAppUrl = "https://developers.giphy.com/dashboard/?create=true";
    private const double StartupSplashBlankSeconds = 1d;
    private const double StartupSplashLoadingSeconds = 2d;
    private const string StartupSplashBlankPath = "embedded://splash-screen-blank.png";
    private const string StartupSplashLoadingPath = "embedded://splash-screen-eorzea.png";
    private readonly Service service;
    private readonly Configuration configuration;
    private readonly PhoneState state;
    private readonly TomestonePhoneClient client;
    private readonly GifEmbedRenderer gifEmbedRenderer;
    private readonly AppIconRenderer appIconRenderer;
    private readonly VoiceChatSession voiceChatSession = new();
    private readonly GiphyClient giphyClient = new();
    private PhoneTab activeTab = PhoneTab.Messages;
    private bool showHomeScreen = true;
    private string loginUsername = string.Empty;
    private string loginPassword = string.Empty;
    private string pendingStatus = "Disconnected";
    private Vector2 lastWindowSize = new(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
    private bool localTermsCheckbox;
    private bool localPrivacyCheckbox;
    private string supportSubject = string.Empty;
    private string supportBody = string.Empty;
    private string oldPassword = string.Empty;
    private string newPassword = string.Empty;
    private string confirmPassword = string.Empty;
    private string deleteAccountPassword = string.Empty;
    private string deleteAccountError = string.Empty;
    private bool openDeleteAccountPasswordPopup;
    private string ownerResetTarget = string.Empty;
    private string ownerResetPassword = string.Empty;
    private AdminDashboardSnapshot? adminDashboard;
    private string staffSearchQuery = string.Empty;
    private string staffTicketParticipantTarget = string.Empty;
    private Guid? selectedConversationId;
    private ConversationMessagePage? selectedConversationMessages;
    private ConversationDetail? selectedConversationDetail;
    private string composeMessage = string.Empty;
    private string composeEmbedUrl = string.Empty;
    private string directMessageTarget = string.Empty;
    private string groupAddTarget = string.Empty;
    private bool showGroupMembersWindow;
    private Guid? pendingGroupRemoveMemberAccountId;
    private string pendingGroupRemoveMemberName = string.Empty;
    private string groupCreateName = string.Empty;
    private string groupCreateTargets = string.Empty;
    private string contactAddTarget = string.Empty;
    private string callTarget = string.Empty;
    private string friendRequestTarget = string.Empty;
    private string friendRequestMessage = string.Empty;
    private string reportReplyBody = string.Empty;
    private string wallpaperImportPath = string.Empty;
    private bool showLinkWarningModal;
    private string pendingExternalUrl = string.Empty;
    private int renderedMessageCount;
    private bool scrollMessagesToBottom = true;
    private bool clearComposeOnNextDraw;
    private bool focusComposeOnNextDraw;
    private int composeControlVersion;
    private Task<ConversationMessagePage>? pendingConversationMessagesTask;
    private DateTimeOffset lastConversationRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastConversationListRefreshUtc = DateTimeOffset.MinValue;
    private Task<AuthResult>? pendingAuthTask;
    private Task<PostAuthSnapshotResult>? pendingSnapshotTask;
    private Task<IReadOnlyList<ActiveCallSessionRecord>>? pendingActiveCallsTask;
    private DateTimeOffset lastSnapshotRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastActiveCallRefreshUtc = DateTimeOffset.MinValue;
    private List<ActiveCallSessionRecord> activeCallSessions = [];
    private HashSet<Guid> seenIncomingDirectCallSessionIds = [];
    private bool refreshOnNextDraw = true;
    private bool snapshotRefreshQueued;
    private bool snapshotRefreshQueuedSilently;
    private bool autoLoginAttempted;
    private string? lastChatDebugMessage;
    private MessageFolder activeMessageFolder = MessageFolder.Regular;

    private Task<ClientVersionPolicyResult>? pendingVersionPolicyTask;
    private bool clientVersionChecked;
    private bool clientUpdateRequired;
    private string minimumClientVersion = string.Empty;
    private string recommendedClientVersion = string.Empty;
    private string clientUpdateMessage = string.Empty;
    private string clientRecommendedMessage = string.Empty;
    private bool clientUpdateNoticeShown;
    private bool clientRecommendedNoticeShown;
    private DateTimeOffset? startupSplashStartedUtc;
    private bool startupSplashCompleted;
    public PhoneWindow(Service service, Configuration configuration, PhoneState state, TomestonePhoneClient client)
        : base("TomestonePhone###TomestonePhoneMain")
    {
        this.service = service;
        this.configuration = configuration;
        this.state = state;
        this.client = client;
        this.gifEmbedRenderer = new GifEmbedRenderer(service.TextureProvider);
        this.appIconRenderer = new AppIconRenderer(service.TextureProvider);
        this.Flags = ImGuiWindowFlags.NoCollapse;
        this.Size = new Vector2(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.lastWindowSize = new Vector2(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
        this.RespectCloseHotkey = true;
    }

    public void OpenSettingsTab()
    {
        this.showHomeScreen = false;
        this.activeTab = PhoneTab.Settings;
    }

    public override void OnOpen()
    {
        this.refreshOnNextDraw = true;
        this.clientUpdateRequired = false;
        this.clientVersionChecked = false;
        this.minimumClientVersion = string.Empty;
        this.recommendedClientVersion = string.Empty;
        this.clientUpdateMessage = string.Empty;
        this.clientRecommendedMessage = string.Empty;
        this.clientUpdateNoticeShown = false;
        this.clientRecommendedNoticeShown = false;
        this.pendingVersionPolicyTask = null;
    }

    public void DisposeResources()
    {
        this.voiceChatSession.Dispose();
        this.gifEmbedRenderer.Dispose();
        this.appIconRenderer.Dispose();
    }

    private float GetUiScale()
    {
        var size = ImGui.GetWindowSize();
        var widthScale = size.X <= 0f ? 1f : size.X / DefaultWindowWidth;
        var heightScale = size.Y <= 0f ? 1f : size.Y / DefaultWindowHeight;
        return Math.Clamp(Math.Min(widthScale, heightScale), MinimumWindowScale, MaximumWindowScale);
    }

    private float Scale(float value)
    {
        return value * this.GetUiScale();
    }

    private Vector2 Scale(float x, float y)
    {
        return new Vector2(this.Scale(x), this.Scale(y));
    }

    private bool IsStaffConversation(ConversationSummary conversation)
    {
        return conversation.IsGroup && string.Equals(conversation.DisplayName, "Staff Room", StringComparison.OrdinalIgnoreCase);
    }
    private bool IsTicketConversation(ConversationSummary conversation)
    {
        return this.state.SupportTickets.Any(ticket => ticket.ConversationId == conversation.Id);
    }

    private IReadOnlyList<ConversationSummary> GetVisibleMessageFolderConversations()
    {
        return this.activeMessageFolder switch
        {
            MessageFolder.Tickets => this.state.Conversations
                .Where(this.IsTicketConversation)
                .OrderByDescending(item => item.LastActivityUtc)
                .ToList(),
            MessageFolder.Staff => this.state.Conversations
                .Where(this.IsStaffConversation)
                .OrderByDescending(item => item.LastActivityUtc)
                .ToList(),
            _ => this.state.Conversations
                .Where(item => !this.IsTicketConversation(item) && !this.IsStaffConversation(item))
                .OrderByDescending(item => item.LastActivityUtc)
                .ToList(),
        };
    }

    private void SyncMessageFolderForConversation(Guid conversationId)
    {
        var conversation = this.state.Conversations.FirstOrDefault(item => item.Id == conversationId);
        if (conversation is null)
        {
            return;
        }

        if (this.IsStaffConversation(conversation))
        {
            this.activeMessageFolder = MessageFolder.Staff;
        }
        else if (this.IsTicketConversation(conversation))
        {
            this.activeMessageFolder = MessageFolder.Tickets;
        }
        else
        {
            this.activeMessageFolder = MessageFolder.Regular;
        }
    }

    public override void Draw()
    {
        this.ProcessBackgroundTasks();
        this.EnsureClientVersionPolicy();
        if (this.clientVersionChecked && !this.clientUpdateRequired)
        {
            this.TryBeginAutoLogin();
            this.EnsureSessionHydrated();
            this.TickMessageAutoRefresh();
            this.TickSnapshotAutoRefresh();
            this.TickActiveCallAutoRefresh();
        }
        this.EnforceAspectRatio();

        var uiScale = this.GetUiScale();
        ImGui.SetWindowFontScale(uiScale);
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(12f * uiScale, 8f * uiScale));
        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(10f * uiScale, 10f * uiScale));
        using var theme = PhoneTheme.Push(this.configuration);
        this.DrawPhoneShell();
        this.DrawTopNotchOverlay();

        if (this.TryGetStartupSplashState(out var showLoadingPhase))
        {
            using var splashRoot = ImRaii.Child("TomestonePhoneRoot", new Vector2(-1f, -1f), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (splashRoot.Success)
            {
                this.DrawStartupSplashScreen(showLoadingPhase);
            }

            return;
        }

        this.DrawNotifications();
        this.DrawLegalModal();
        this.DrawPrivacyModal();
        this.DrawOpenEmoteSetupModal();
        this.DrawExternalLinkWarningModal();

        using var root = ImRaii.Child("TomestonePhoneRoot", new Vector2(-1f, -1f), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!root.Success)
        {
            return;
        }

        this.DrawCallBanner();
        this.DrawHeader();
        ImGui.Separator();

        var footerHeight = this.Scale(52f);
        var contentHeight = Math.Max(this.Scale(120f), ImGui.GetContentRegionAvail().Y - footerHeight);
        using (var content = ImRaii.Child("TomestonePhoneContent", new Vector2(-1f, contentHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (!content.Success)
            {
                return;
            }

            if (!this.clientVersionChecked)
            {
                this.DrawClientVersionCheckScreen();
            }
            else if (this.clientUpdateRequired)
            {
                this.DrawClientUpdateRequiredScreen();
            }
            else if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                if (this.pendingAuthTask is { IsCompleted: false })
                {
                    this.DrawSessionRestoreScreen();
                }
                else
                {
                    this.DrawAuthStartScreen();
                }
            }
            else if (!this.HasHydratedAuthenticatedProfile())
            {
                if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false } || this.refreshOnNextDraw)
                {
                    this.DrawSessionRestoreScreen();
                }
                else if (this.showHomeScreen)
                {
                    this.DrawHomeScreen();
                }
                else
                {
                    this.activeTab = PhoneTab.Settings;
                    this.DrawSettings();
                }
            }
            else if (this.showHomeScreen)
            {
                this.DrawHomeScreen();
            }
            else
            {
                switch (this.activeTab)
                {
                    case PhoneTab.Messages:
                        this.DrawMessages();
                        break;
                    case PhoneTab.Calls:
                        this.DrawCalls();
                        break;
                    case PhoneTab.Contacts:
                        this.DrawContacts();
                        break;
                    case PhoneTab.Friends:
                        this.DrawFriends();
                        break;
                    case PhoneTab.Settings:
                        this.DrawSettings();
                        break;
                    case PhoneTab.Legal:
                        this.DrawLegalApp();
                        break;
                    case PhoneTab.Privacy:
                        this.DrawPrivacyApp();
                        break;
                    case PhoneTab.Support:
                        this.DrawSupportApp();
                        break;
                    case PhoneTab.Staff:
                        this.DrawStaffApp();
                        break;
                    default:
                        this.DrawHomeScreen();
                        break;
                }
            }
        }

        using (var footer = ImRaii.Child("TomestonePhoneFooter", new Vector2(-1f, footerHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (footer.Success)
            {
                this.DrawHomeButton();
            }
        }
    }

    private bool TryGetStartupSplashState(out bool showLoadingPhase)
    {
        showLoadingPhase = false;
        if (this.startupSplashCompleted)
        {
            return false;
        }

        this.startupSplashStartedUtc ??= DateTimeOffset.UtcNow;
        var elapsedSeconds = (DateTimeOffset.UtcNow - this.startupSplashStartedUtc.Value).TotalSeconds;
        if (elapsedSeconds >= StartupSplashBlankSeconds + StartupSplashLoadingSeconds)
        {
            this.startupSplashCompleted = true;
            return false;
        }

        showLoadingPhase = elapsedSeconds >= StartupSplashBlankSeconds;
        return true;
    }

    private void DrawStartupSplashScreen(bool showLoadingPhase)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.07f, 1f)));
        ImGui.InvisibleButton("StartupSplashFill", size);

        var splashPath = showLoadingPhase ? StartupSplashLoadingPath : StartupSplashBlankPath;
        var splash = this.appIconRenderer.TryGetTexture(splashPath);
        if (splash is not null)
        {
            drawList.AddImage(splash.Handle, origin, origin + size);
            return;
        }

        if (!showLoadingPhase)
        {
            return;
        }

        var loadingText = "Loading...";
        var loadingSize = ImGui.CalcTextSize(loadingText);
        var loadingPos = new Vector2(origin.X + ((size.X - loadingSize.X) * 0.5f), origin.Y + (size.Y * 0.78f));
        drawList.AddText(loadingPos, ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.88f, 0.92f)), loadingText);
    }

    private void EnsureClientVersionPolicy()
    {
        if (this.clientVersionChecked || this.pendingVersionPolicyTask is not null)
        {
            return;
        }

        this.pendingVersionPolicyTask = this.client.GetVersionPolicyAsync();
    }

    private void DrawClientVersionCheckScreen()
    {
        ImGui.TextDisabled("Checking client version...");
        ImGui.Spacing();
        ImGui.TextWrapped("TomestonePhone is checking whether this plugin build is still allowed by the server.");
    }

    private void DrawClientUpdateRequiredScreen()
    {
        ImGui.TextDisabled("Update Required");
        ImGui.Spacing();
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(this.clientUpdateMessage)
            ? "Please update TomestonePhone to the latest version before using the app."
            : this.clientUpdateMessage);
        if (!string.IsNullOrWhiteSpace(this.minimumClientVersion))
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Minimum allowed version: {this.minimumClientVersion}");
            ImGui.TextDisabled($"Your version: {this.GetCurrentClientVersion()}");
        }
    }

    private string GetCurrentClientVersion()
    {
        return GetType().Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0";
    }

    private bool IsClientVersionOutdated(string minimumVersion)
    {
        if (!Version.TryParse(minimumVersion, out var minimum))
        {
            return false;
        }

        if (!Version.TryParse(this.GetCurrentClientVersion(), out var current))
        {
            return false;
        }

        return current < minimum;
    }
    private void DrawHeader()
    {
        var topStart = ImGui.GetCursorScreenPos();
        var topWidth = ImGui.GetContentRegionAvail().X;
        var topHeight = this.Scale(48f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(22f));
        draw.AddRect(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(22f));

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(10f)));
        ImGui.TextDisabled(DateTime.Now.ToString("h:mm"));
        var rightLabel = "Aether   |||   88%";
        var rightSize = ImGui.CalcTextSize(rightLabel);
        ImGui.SameLine(topWidth - rightSize.X - this.Scale(18f));
        ImGui.TextDisabled(rightLabel);

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(23f)));
        var title = string.IsNullOrWhiteSpace(this.configuration.AuthToken)
            ? "TomestonePhone"
            : this.showHomeScreen ? this.state.CurrentProfile.DisplayName : this.activeTab.ToString();
        ImGui.TextUnformatted(title);

        if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            var refreshWidth = this.Scale(78f);
            ImGui.SameLine(topWidth - refreshWidth - this.Scale(14f));
            if (ImGui.Button("Refresh", new Vector2(refreshWidth, this.Scale(24f))))
            {
                this.refreshOnNextDraw = true;
                this.RefreshSnapshot();
            }
        }

        ImGui.TextDisabled(this.pendingStatus);
        ImGui.Dummy(new Vector2(topWidth, topHeight - this.Scale(6f)));
    }

    private void DrawAuthStartScreen()
    {
        using var panel = ImRaii.Child("auth-start", new Vector2(-1f, -1f), true);
        if (!panel.Success)
        {
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;
        using (var hero = ImRaii.Child("auth-hero", new Vector2(-1f, this.Scale(148f)), false))
        {
            if (hero.Success)
            {
                var draw = ImGui.GetWindowDrawList();
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(width, this.Scale(148f));
                draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(28f));
                draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(28f));
                draw.AddCircleFilled(min + new Vector2(width - this.Scale(42f), this.Scale(26f)), this.Scale(54f), ImGui.GetColorU32(new Vector4(0.96f, 0.72f, 0.45f, 0.12f)), 48);
                ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
                ImGui.TextUnformatted("Welcome");
                ImGui.TextWrapped("Sign in or create your TomestonePhone account before using messages, calls, contacts, and support.");
                ImGui.Dummy(new Vector2(0f, this.Scale(4f)));
                ImGui.TextDisabled("Your account and phone number are restored automatically on this device once you sign in.");
            }
        }

        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        using (var account = ImRaii.Child("auth-account-card", new Vector2(-1f, this.Scale(160f)), false))
        {
            if (account.Success)
            {
                ImGui.TextDisabled("Account");
                ImGui.InputTextWithHint("##auth-username", "Username", ref this.loginUsername, 64);
                ImGui.InputTextWithHint("##auth-password", "Password", ref this.loginPassword, 64, ImGuiInputTextFlags.Password);
                var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
                if (ImGui.Button("Create Account", new Vector2(actionWidth, this.Scale(36f))))
                {
                    this.BeginRegister();
                }
                ImGui.SameLine();
                if (ImGui.Button("Sign In", new Vector2(actionWidth, this.Scale(36f))))
                {
                    this.BeginLogin();
                }
            }
        }

        using (var legal = ImRaii.Child("auth-legal-card", new Vector2(-1f, this.Scale(94f)), false))
        {
            if (legal.Success)
            {
                ImGui.TextDisabled("Before you continue");
                ImGui.TextWrapped("Terms and Privacy stay available inside the phone at any time.");
                var legalButtonWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
                if (ImGui.Button("Terms", new Vector2(legalButtonWidth, this.Scale(34f))))
                {
                    this.activeTab = PhoneTab.Legal;
                }
                ImGui.SameLine();
                if (ImGui.Button("Privacy", new Vector2(legalButtonWidth, this.Scale(34f))))
                {
                    this.activeTab = PhoneTab.Privacy;
                }
            }
        }
    }

    private void DrawHomeScreen()
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var totalHeight = ImGui.GetContentRegionAvail().Y;
        var columns = 3;
        var spacing = this.Scale(12f);
        var sideInset = this.Scale(6f);
        var topInset = this.Scale(10f);
        var bottomInset = this.Scale(8f);
        var dockHeight = this.Scale(148f);
        var gridApps = new List<(string Label, string Glyph, PhoneTab Tab, int Badge)>
        {
            ("Friends", "F", PhoneTab.Friends, this.state.FriendRequests.Count(item => item.Status == FriendRequestStatus.Pending)),
            ("Settings", "S", PhoneTab.Settings, 0),
            ("Legal", "L", PhoneTab.Legal, 0),
            ("Privacy", "P", PhoneTab.Privacy, 0),
            ("Support", "?", PhoneTab.Support, 0)
        };

        if (this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator)
        {
            gridApps.Add(("Staff", "A", PhoneTab.Staff, this.state.VisibleReports.Count(item => item.Status == ReportStatus.Open)));
        }

        var rows = Math.Max(1, (int)Math.Ceiling(gridApps.Count / (float)columns));
        var usableHeight = Math.Max(this.Scale(180f), totalHeight - dockHeight - topInset - bottomInset - spacing);
        var cellWidth = (totalWidth - sideInset * 2f - spacing * (columns - 1)) / columns;
        var cellHeight = (usableHeight - spacing * Math.Max(0, rows - 1)) / rows;
        var cell = MathF.Min(cellWidth, cellHeight);
        var gridWidth = cell * columns + spacing * (columns - 1);
        var gridStartX = ImGui.GetCursorPosX() + Math.Max(0f, (totalWidth - gridWidth) * 0.5f);
        var totalGridHeight = rows * cell + Math.Max(0, rows - 1) * spacing;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + topInset);
        for (var index = 0; index < gridApps.Count; index++)
        {
            if (index % columns == 0)
            {
                ImGui.SetCursorPosX(gridStartX);
            }

            var app = gridApps[index];
            this.DrawAppIcon(app.Label, app.Glyph, app.Tab, app.Badge, cell, Vector4.Zero, Vector4.Zero);
            if (index % columns < columns - 1 && index < gridApps.Count - 1)
            {
                ImGui.SameLine(0f, spacing);
            }
        }

        var spacerHeight = Math.Max(0f, totalHeight - topInset - totalGridHeight - dockHeight - bottomInset);
        if (spacerHeight > 0f)
        {
            ImGui.Dummy(new Vector2(0f, spacerHeight));
        }

        this.DrawDock();
    }

    private string GetAppIconPath(PhoneTab tab)
    {
        return tab switch
        {
            PhoneTab.Messages => this.configuration.MessagesIconPath,
            PhoneTab.Calls => this.configuration.CallsIconPath,
            PhoneTab.Contacts => this.configuration.ContactsIconPath,
            PhoneTab.Friends => this.configuration.FriendsIconPath,
            PhoneTab.Settings => this.configuration.SettingsIconPath,
            PhoneTab.Legal => this.configuration.LegalIconPath,
            PhoneTab.Privacy => this.configuration.PrivacyIconPath,
            PhoneTab.Support => this.configuration.SupportIconPath,
            PhoneTab.Staff => this.configuration.StaffIconPath,
            _ => string.Empty,
        };
    }

    private void DrawAppIcon(string label, string glyph, PhoneTab tab, int badgeCount, float width, Vector4 topColor, Vector4 bottomColor)
    {
        var cardHeight = width;
        using var group = ImRaii.Child($"app-{label}", new Vector2(width, cardHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!group.Success)
        {
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var iconSize = Math.Min(width * 0.72f, this.Scale(112f));
        var iconTop = this.Scale(8f);
        var labelBottomPadding = this.Scale(12f);
        var labelSize = ImGui.CalcTextSize(label);
        var iconMin = pos + new Vector2((width - iconSize) * 0.5f, iconTop);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var cardMin = pos + new Vector2(this.Scale(2f), this.Scale(2f));
        var cardMax = pos + new Vector2(width - this.Scale(2f), cardHeight - this.Scale(4f));
        draw.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.028f)), this.Scale(24f));
        draw.AddRect(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), this.Scale(24f));

        var iconTexture = this.appIconRenderer.TryGetIcon(this.GetAppIconPath(tab));
        if (iconTexture is not null)
        {
            draw.AddImageRounded(iconTexture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), this.Scale(18f));
        }
        else
        {
            draw.AddRectFilledMultiColor(iconMin, iconMax, ImGui.GetColorU32(topColor), ImGui.GetColorU32(topColor), ImGui.GetColorU32(bottomColor), ImGui.GetColorU32(bottomColor));
            draw.AddRect(iconMin, iconMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), this.Scale(22f), ImDrawFlags.None, 1.2f);
            var glyphSize = ImGui.CalcTextSize(glyph);
            draw.AddText(new Vector2(iconMin.X + (iconSize - glyphSize.X) * 0.5f, iconMin.Y + (iconSize - glyphSize.Y) * 0.5f - this.Scale(1f)), ImGui.GetColorU32(Vector4.One), glyph);
        }

        if (badgeCount > 0)
        {
            var badgeCenter = new Vector2(iconMax.X - this.Scale(4f), iconMin.Y + this.Scale(4f));
            draw.AddCircleFilled(badgeCenter, this.Scale(13f), ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.25f, 1f)));
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeTextSize = ImGui.CalcTextSize(badgeText);
            draw.AddText(new Vector2(badgeCenter.X - badgeTextSize.X * 0.5f, badgeCenter.Y - badgeTextSize.Y * 0.5f), ImGui.GetColorU32(Vector4.One), badgeText);
        }

        if (ImGui.InvisibleButton($"{label}##open", new Vector2(width, cardHeight)))
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }

        draw.AddText(new Vector2(pos.X + (width - labelSize.X) * 0.5f, pos.Y + cardHeight - labelBottomPadding - labelSize.Y), ImGui.GetColorU32(Vector4.One), label);
    }

    private void DrawDock()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos();
        var spacing = this.Scale(18f);
        var horizontalInset = this.Scale(18f);
        var cellWidth = (width - horizontalInset * 2f - spacing * 2f) / 3f;
        var iconSize = this.Scale(58f);
        var dockHeight = iconSize + this.Scale(48f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(30f));
        draw.AddRect(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(30f));

        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 0, "Calls", "C", PhoneTab.Calls, this.state.MissedCallCount, new Vector4(0.23f, 0.83f, 0.57f, 1f), new Vector4(0.12f, 0.56f, 0.37f, 1f));
        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 1, "Contacts", "P", PhoneTab.Contacts, 0, new Vector4(0.98f, 0.62f, 0.39f, 1f), new Vector4(0.86f, 0.43f, 0.22f, 1f));
        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 2, "Messages", "M", PhoneTab.Messages, this.state.UnreadConversationCount, new Vector4(0.28f, 0.6f, 0.98f, 1f), new Vector4(0.17f, 0.36f, 0.8f, 1f));
    }

    private void DrawDockIcon(Vector2 dockStart, float horizontalInset, float spacing, float cellWidth, float iconSize, int index, string label, string glyph, PhoneTab tab, int badgeCount, Vector4 topColor, Vector4 bottomColor)
    {
        var draw = ImGui.GetWindowDrawList();
        var x = dockStart.X + horizontalInset + index * (cellWidth + spacing);
        var y = dockStart.Y - this.Scale(12f);
        var iconMin = new Vector2(x + (cellWidth - iconSize) * 0.5f, y);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconTexture = this.appIconRenderer.TryGetIcon(this.GetAppIconPath(tab));
        if (iconTexture is not null)
        {
            draw.AddImageRounded(iconTexture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), this.Scale(18f));
        }
        else
        {
            draw.AddRectFilledMultiColor(iconMin, iconMax, ImGui.GetColorU32(topColor), ImGui.GetColorU32(topColor), ImGui.GetColorU32(bottomColor), ImGui.GetColorU32(bottomColor));
            var glyphSize = ImGui.CalcTextSize(glyph);
            draw.AddText(new Vector2(iconMin.X + (iconSize - glyphSize.X) * 0.5f, iconMin.Y + (iconSize - glyphSize.Y) * 0.5f), ImGui.GetColorU32(Vector4.One), glyph);
        }

        if (badgeCount > 0)
        {
            var badgeCenter = new Vector2(iconMax.X - this.Scale(3f), iconMin.Y + this.Scale(3f));
            draw.AddCircleFilled(badgeCenter, this.Scale(11f), ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.25f, 1f)));
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeTextSize = ImGui.CalcTextSize(badgeText);
            draw.AddText(new Vector2(badgeCenter.X - badgeTextSize.X * 0.5f, badgeCenter.Y - badgeTextSize.Y * 0.5f), ImGui.GetColorU32(Vector4.One), badgeText);
        }

        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(x + (cellWidth - labelSize.X) * 0.5f, iconMax.Y + this.Scale(8f)), ImGui.GetColorU32(Vector4.One), label);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (ImGui.InvisibleButton($"{label}##dock", new Vector2(cellWidth, iconSize + this.Scale(34f))))
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }
    }

    private void DrawMessages()
    {
        if (this.selectedConversationId is { } selectedId && this.selectedConversationMessages is not null)
        {
            var detailHeight = this.selectedConversationDetail is null ? 0f : this.Scale(this.selectedConversationDetail.LinkedSupportTicketId is not null ? 96f : this.selectedConversationDetail.IsGroup ? 64f : 52f);
            var composerHeight = this.Scale(92f);
            var threadHeight = Math.Max(this.Scale(180f), ImGui.GetContentRegionAvail().Y - detailHeight - composerHeight - this.Scale(8f));

            if (this.selectedConversationDetail is not null)
            {
                using var details = ImRaii.Child("messages-detail-card", new Vector2(-1f, detailHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                if (details.Success)
                {
                    if (ImGui.Button("Back To List", this.Scale(120f, 30f)))
                    {
                        this.selectedConversationId = null;
                        this.selectedConversationMessages = null;
                        this.selectedConversationDetail = null;
                        this.showGroupMembersWindow = false;
                        this.groupAddTarget = string.Empty;
                        this.pendingGroupRemoveMemberAccountId = null;
                        this.pendingGroupRemoveMemberName = string.Empty;
                        this.staffTicketParticipantTarget = string.Empty;
                        return;
                    }
                    ImGui.TextDisabled(this.selectedConversationDetail.Name);
                    if (this.selectedConversationDetail.IsGroup)
                    {
                        var ownsConversation = this.selectedConversationDetail.Members.Any(item => item.AccountId == this.state.CurrentProfile.AccountId && item.Role == GroupMemberRole.Owner);
                        var membersButtonWidth = this.Scale(138f);
                        var membersButtonX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - membersButtonWidth);
                        ImGui.SetCursorPosX(membersButtonX);
                        if (ImGui.Button("Members List", new Vector2(membersButtonWidth, this.Scale(28f))))
                        {
                            this.showGroupMembersWindow = true;
                        }

                        if (this.showGroupMembersWindow)
                        {
                            var membersWindowOpen = true;
                            ImGui.SetNextWindowSize(this.Scale(460f, 520f), ImGuiCond.Appearing);
                            if (ImGui.Begin("Group Members", ref membersWindowOpen, ImGuiWindowFlags.NoCollapse))
                            {
                                ImGui.TextDisabled("Manage Members");
                                ImGui.Separator();

                                var addButtonWidth = this.Scale(96f);
                                ImGui.SetNextItemWidth(Math.Max(this.Scale(180f), ImGui.GetContentRegionAvail().X - addButtonWidth - this.Scale(12f)));
                                ImGui.InputTextWithHint("##group-popup-add-target", "Username or phone number", ref this.groupAddTarget, 64);
                                ImGui.SameLine();
                                if (ImGui.Button("Add Member", new Vector2(addButtonWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.groupAddTarget) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                                {
                                    try
                                    {
                                        var lookupConversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(this.groupAddTarget)).GetAwaiter().GetResult();
                                        var lookupDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, lookupConversation.Id).GetAwaiter().GetResult();
                                        var targetMember = lookupDetail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId);
                                        if (targetMember is null)
                                        {
                                            this.pendingStatus = "Person could not be resolved";
                                        }
                                        else
                                        {
                                            var updated = this.client.ModerateConversationAsync(this.configuration.AuthToken, new ConversationModerationRequest(selectedId, ChatModerationAction.AddMember, targetMember.AccountId)).GetAwaiter().GetResult();
                                            if (updated is null)
                                            {
                                                this.pendingStatus = "Could not add member";
                                            }
                                            else
                                            {
                                                this.selectedConversationDetail = updated;
                                                this.groupAddTarget = string.Empty;
                                                this.RefreshSnapshot();
                                                this.pendingStatus = "Member added";
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                                    }
                                }

                                var suggestedFriends = this.state.Friends
                                    .Where(item => this.selectedConversationDetail.Members.All(member => member.AccountId != item.FriendAccountId))
                                    .Where(item => string.IsNullOrWhiteSpace(this.groupAddTarget)
                                        || item.FriendDisplayName.Contains(this.groupAddTarget, StringComparison.OrdinalIgnoreCase)
                                        || item.FriendPhoneNumber.Contains(this.groupAddTarget, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(item => item.FriendDisplayName)
                                    .Take(8)
                                    .ToList();
                                if (suggestedFriends.Count > 0)
                                {
                                    ImGui.Spacing();
                                    ImGui.TextDisabled("Friends");
                                    using var friendList = ImRaii.Child("group-member-friends", new Vector2(-1f, this.Scale(120f)), true);
                                    if (friendList.Success)
                                    {
                                        foreach (var friend in suggestedFriends)
                                        {
                                            ImGui.TextUnformatted(friend.FriendDisplayName);
                                            ImGui.SameLine();
                                            ImGui.TextDisabled(friend.FriendPhoneNumber);
                                            var quickAddWidth = this.Scale(94f);
                                            var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - quickAddWidth);
                                            ImGui.SetCursorPosX(maxX);
                                            if (ImGui.Button($"Add##friend-{friend.FriendAccountId}", new Vector2(quickAddWidth, this.Scale(26f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                                            {
                                                try
                                                {
                                                    var updated = this.client.ModerateConversationAsync(this.configuration.AuthToken, new ConversationModerationRequest(selectedId, ChatModerationAction.AddMember, friend.FriendAccountId)).GetAwaiter().GetResult();
                                                    if (updated is not null)
                                                    {
                                                        this.selectedConversationDetail = updated;
                                                        this.RefreshSnapshot();
                                                        this.pendingStatus = "Member added";
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                                                }
                                            }
                                            ImGui.Separator();
                                        }
                                    }
                                }

                                ImGui.Spacing();
                                ImGui.TextDisabled("Current Members");
                                using var memberList = ImRaii.Child("group-members-popup-list", new Vector2(-1f, this.Scale(220f)), true);
                                if (memberList.Success)
                                {
                                    foreach (var member in this.selectedConversationDetail.Members.OrderByDescending(item => item.Role).ThenBy(item => item.DisplayName))
                                    {
                                        ImGui.TextUnformatted(member.DisplayName);
                                        ImGui.SameLine();
                                        ImGui.TextDisabled($"[{member.Role}]");
                                        if (!string.IsNullOrWhiteSpace(member.PhoneNumber))
                                        {
                                            ImGui.TextDisabled(member.PhoneNumber);
                                        }
                                        if (member.AccountId != this.state.CurrentProfile.AccountId && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                                        {
                                            var actionWidth = ownsConversation ? this.Scale(72f) : this.Scale(84f);
                                            var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - actionWidth);
                                            ImGui.SetCursorPosX(maxX);
                                            if (ImGui.Button($"Friend##member-{member.AccountId}", new Vector2(actionWidth, this.Scale(26f))))
                                            {
                                                try
                                                {
                                                    this.client.CreateFriendRequestAsync(this.configuration.AuthToken, new FriendRequestCreateRequest(member.PhoneNumber, null)).GetAwaiter().GetResult();
                                                    this.pendingStatus = "Friend request sent";
                                                    this.RefreshSnapshot();
                                                }
                                                catch (Exception ex)
                                                {
                                                    this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                                                }
                                            }

                                            if (ownsConversation)
                                            {
                                                ImGui.SameLine();
                                                if (ImGui.Button($"X##member-{member.AccountId}", new Vector2(this.Scale(28f), this.Scale(26f))))
                                                {
                                                    this.pendingGroupRemoveMemberAccountId = member.AccountId;
                                                    this.pendingGroupRemoveMemberName = member.DisplayName;
                                                    ImGui.OpenPopup("confirm-remove-group-member");
                                                }
                                            }
                                        }
                                        ImGui.Separator();
                                    }
                                }

                                if (ImGui.BeginPopupModal("confirm-remove-group-member", ImGuiWindowFlags.AlwaysAutoResize))
                                {
                                    ImGui.TextWrapped($"Remove {this.pendingGroupRemoveMemberName} from this group? This cannot be undone automatically.");
                                    if (ImGui.Button("Cancel", this.Scale(110f, 30f)))
                                    {
                                        this.pendingGroupRemoveMemberAccountId = null;
                                        this.pendingGroupRemoveMemberName = string.Empty;
                                        ImGui.CloseCurrentPopup();
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button("Remove", this.Scale(110f, 30f)) && this.pendingGroupRemoveMemberAccountId is Guid removeId && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                                    {
                                        try
                                        {
                                            var updated = this.client.ModerateConversationAsync(this.configuration.AuthToken, new ConversationModerationRequest(selectedId, ChatModerationAction.RemoveMember, removeId)).GetAwaiter().GetResult();
                                            if (updated is not null)
                                            {
                                                this.selectedConversationDetail = updated;
                                                this.RefreshSnapshot();
                                                this.pendingStatus = "Member removed";
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                                        }

                                        this.pendingGroupRemoveMemberAccountId = null;
                                        this.pendingGroupRemoveMemberName = string.Empty;
                                        ImGui.CloseCurrentPopup();
                                    }
                                    ImGui.EndPopup();
                                }

                                ImGui.End();
                            }
                            this.showGroupMembersWindow = membersWindowOpen;
                        }
                    }

                    var linkedTicketId = this.selectedConversationDetail.LinkedSupportTicketId;
                    var isSupportConversation = linkedTicketId is not null;
                    var isStaff = this.IsCurrentUserStaff();
                    var activeSession = this.GetConversationActiveCallSession(selectedId);
                    if (activeSession is not null)
                    {
                        var durationLabel = (DateTimeOffset.UtcNow - activeSession.StartedUtc).ToString(@"hh\:mm\:ss");
                        ImGui.TextDisabled($"Active call - {durationLabel}");
                        var activeCallLabel = activeSession.IncludesCurrentAccount
                            ? (this.IsCurrentCallSession(activeSession.Id) ? (activeSession.IsGroup ? "Leave Call" : "End Call") : "Resume Call")
                            : "Join Call";
                        if (ImGui.Button(activeCallLabel, new Vector2(this.Scale(132f), this.Scale(30f))))
                        {
                            if (activeSession.IncludesCurrentAccount && this.IsCurrentCallSession(activeSession.Id))
                            {
                                this.LeaveCurrentCall();
                            }
                            else if (activeSession.IncludesCurrentAccount)
                            {
                                this.state.ActiveCall = this.MapActiveCallState(activeSession);
                                this.ConnectVoiceToCurrentCall();
                                this.pendingStatus = $"Resumed {activeSession.DisplayName}";
                            }
                            else
                            {
                                this.BeginConversationCall(selectedId, activeSession.IsGroup);
                            }
                        }
                    }
                    else if (this.selectedConversationDetail.IsGroup)
                    {
                        if (ImGui.Button("Start Group Call", new Vector2(this.Scale(148f), this.Scale(30f))))
                        {
                            this.BeginConversationCall(selectedId, true);
                        }
                    }

                    if (isSupportConversation && linkedTicketId is Guid ticketId && isStaff)
                    {
                        var actionWidth = this.Scale(100f);
                        var firstRowWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - actionWidth * 2f - this.Scale(20f));
                        ImGui.SetNextItemWidth(firstRowWidth);
                        ImGui.InputTextWithHint("##support-ticket-participant", "Add by username or phone number", ref this.staffTicketParticipantTarget, 64);
                        ImGui.SameLine();
                        if (ImGui.Button("Add Person", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            var targetAccountId = this.ResolveSingleConversationTarget(this.staffTicketParticipantTarget);
                            if (targetAccountId is null)
                            {
                                this.pendingStatus = "Person could not be resolved";
                            }
                            else
                            {
                                var updatedTicket = this.client.AddSupportTicketParticipantAsync(this.configuration.AuthToken!, ticketId, targetAccountId.Value).GetAwaiter().GetResult();
                                if (updatedTicket is null)
                                {
                                    this.pendingStatus = "Could not add participant";
                                }
                                else
                                {
                                    this.UpsertSupportTicket(updatedTicket);
                                    this.staffTicketParticipantTarget = string.Empty;
                                    this.OpenConversation(updatedTicket.ConversationId);
                                    this.RefreshSnapshot();
                                    this.RefreshStaffDashboard();
                                    this.pendingStatus = "Participant added";
                                }
                            }
                        }
                        ImGui.SameLine();
                        if (this.selectedConversationDetail.IsReadOnly)
                        {
                            ImGui.BeginDisabled();
                            ImGui.Button("Closed", new Vector2(actionWidth, this.Scale(30f)));
                            ImGui.EndDisabled();
                        }
                        else if (ImGui.Button("Close Ticket", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            var updatedTicket = this.client.CloseSupportTicketAsync(this.configuration.AuthToken!, ticketId).GetAwaiter().GetResult();
                            if (updatedTicket is null)
                            {
                                this.pendingStatus = "Could not close ticket";
                            }
                            else
                            {
                                this.UpsertSupportTicket(updatedTicket);
                                this.OpenConversation(updatedTicket.ConversationId);
                                this.RefreshSnapshot();
                                this.RefreshStaffDashboard();
                                this.pendingStatus = "Ticket closed";
                            }
                        }
                    }
                    else if (!this.selectedConversationDetail.IsGroup && this.selectedConversationDetail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId) is { } otherMember && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(24f)) / 3f;
                        if (ImGui.Button("Save Contact", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            var contact = this.client.AddContactAsync(this.configuration.AuthToken, otherMember.AccountId, otherMember.DisplayName, string.Empty).GetAwaiter().GetResult();
                            this.state.Contacts.RemoveAll(item => item.Id == contact.Id);
                            this.state.Contacts.Add(contact);
                            this.pendingStatus = "Contact saved";
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Call", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            this.BeginConversationCall(selectedId, false);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Block", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            var success = this.client.BlockAccountAsync(this.configuration.AuthToken, otherMember.AccountId).GetAwaiter().GetResult();
                            this.pendingStatus = success ? "Blocked" : "Block failed";
                        }
                    }
                }
            }
            var selectedMessages = this.selectedConversationMessages;
            using (var scroll = ImRaii.Child("message-thread", new Vector2(-1f, threadHeight), true))
            {
                if (scroll.Success && selectedMessages is not null)
                {
                    var currentMessages = selectedMessages.Messages;
                    var currentCount = currentMessages.Count;
                    if (currentCount != this.renderedMessageCount)
                    {
                        this.renderedMessageCount = currentCount;
                        this.scrollMessagesToBottom = true;
                    }

                    foreach (var message in currentMessages)
                    {
                        this.DrawMessageBubble(message);
                        ImGui.Dummy(new Vector2(0f, this.Scale(4f)));
                    }

                    if (this.scrollMessagesToBottom)
                    {
                        ImGui.SetScrollHereY(1f);
                        this.scrollMessagesToBottom = false;
                    }
                }
            }

            using (var composer = ImRaii.Child("message-compose-card", new Vector2(-1f, composerHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (composer.Success)
                {
                    if (this.selectedConversationDetail?.IsReadOnly == true)
                    {
                        ImGui.TextDisabled("This ticket is closed. You can still read the log, but no new messages can be sent.");
                    }
                    else
                    {
                        if (this.focusComposeOnNextDraw)
                        {
                            ImGui.SetKeyboardFocusHere();
                            this.focusComposeOnNextDraw = false;
                        }
                        if (this.clearComposeOnNextDraw)
                        {
                            this.composeMessage = string.Empty;
                            this.clearComposeOnNextDraw = false;
                        }
                        ImGui.InputTextMultiline($"##message-compose-{this.composeControlVersion}", ref this.composeMessage, 1024, new Vector2(-1f, this.Scale(58f)));
                        var sendPressed = ImGui.IsItemActive() &&
                            (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));
                        if (sendPressed && !ImGui.GetIO().KeyShift)
                        {
                            this.composeMessage = this.composeMessage.TrimEnd('\r', '\n');
                            this.SendComposedMessage(selectedId);
                        }
                        ImGui.TextDisabled("Enter sends. Shift+Enter adds a new line.");
                    }
                }
            }

            return;
        }

        using (var compose = ImRaii.Child("messages-compose-card", new Vector2(-1f, this.Scale(196f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (compose.Success)
            {
                var ticketUnread = this.state.SupportTickets.Sum(ticket => this.state.Conversations.FirstOrDefault(item => item.Id == ticket.ConversationId)?.UnreadCount ?? 0);
                var staffUnread = this.state.Conversations.Where(this.IsStaffConversation).Sum(item => item.UnreadCount);
                var regularUnread = this.state.Conversations.Where(item => !this.IsTicketConversation(item) && !this.IsStaffConversation(item)).Sum(item => item.UnreadCount);
                var tabWidth = this.IsCurrentUserStaff()
                    ? (ImGui.GetContentRegionAvail().X - this.Scale(20f)) / 3f
                    : (ImGui.GetContentRegionAvail().X - this.Scale(10f)) / 2f;
                if (ImGui.Button(regularUnread > 0 ? $"Regular [{regularUnread}]" : "Regular", new Vector2(tabWidth, this.Scale(30f))))
                {
                    this.activeMessageFolder = MessageFolder.Regular;
                }
                ImGui.SameLine();
                if (ImGui.Button(ticketUnread > 0 ? $"Tickets [{ticketUnread}]" : "Tickets", new Vector2(tabWidth, this.Scale(30f))))
                {
                    this.activeMessageFolder = MessageFolder.Tickets;
                }
                if (this.IsCurrentUserStaff())
                {
                    ImGui.SameLine();
                    if (ImGui.Button(staffUnread > 0 ? $"Staff [{staffUnread}]" : "Staff", new Vector2(tabWidth, this.Scale(30f))))
                    {
                        this.activeMessageFolder = MessageFolder.Staff;
                    }
                }
                var buttonWidth = this.Scale(112f);
                var inputWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - buttonWidth - this.Scale(10f));
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputTextWithHint("##direct-target", "Username or phone number", ref this.directMessageTarget, 64);
                ImGui.SameLine();
                if (ImGui.Button("New Chat", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.directMessageTarget))
                {
                    try
                    {
                        var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(this.directMessageTarget)).GetAwaiter().GetResult();
                        this.directMessageTarget = string.Empty;
                        this.RefreshSnapshot();
                        this.selectedConversationId = conversation.Id;
                        this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                        this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                        this.SyncMessageFolderForConversation(conversation.Id);
                        this.renderedMessageCount = 0;
                        this.scrollMessagesToBottom = true;
                        this.pendingStatus = "Conversation ready";
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = ex.Message;
                    }
                }
                if (this.activeMessageFolder == MessageFolder.Regular)
                {
                    var groupButtonWidth = this.Scale(112f);
                    var groupNameWidth = Math.Max(this.Scale(120f), ImGui.GetContentRegionAvail().X - groupButtonWidth - this.Scale(10f));
                    ImGui.SetNextItemWidth(groupNameWidth);
                    ImGui.InputTextWithHint("##group-name", "Group name", ref this.groupCreateName, 64);
                    ImGui.SameLine();
                    if (ImGui.Button("New Group", new Vector2(groupButtonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.groupCreateName) && !string.IsNullOrWhiteSpace(this.groupCreateTargets))
                    {
                        try
                        {
                            var participantIds = this.ResolveConversationTargets(this.groupCreateTargets).ToList();
                            if (participantIds.Count == 0)
                            {
                                this.pendingStatus = "Add at least one valid member";
                            }
                            else
                            {
                                var conversation = this.client.CreateConversationAsync(this.configuration.AuthToken, new CreateConversationRequest(this.groupCreateName.Trim(), true, participantIds)).GetAwaiter().GetResult();
                                this.groupCreateName = string.Empty;
                                this.groupCreateTargets = string.Empty;
                                this.RefreshSnapshot();
                                this.selectedConversationId = conversation.Id;
                                this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                                this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                                this.SyncMessageFolderForConversation(conversation.Id);
                                this.renderedMessageCount = 0;
                                this.scrollMessagesToBottom = true;
                                this.pendingStatus = "Group ready";
                            }
                        }
                        catch (Exception ex)
                        {
                            this.pendingStatus = ex.Message;
                        }
                    }
                    ImGui.InputTextWithHint("##group-members", "Members, comma separated", ref this.groupCreateTargets, 256);
                }
            }
        }
        var listHeight = Math.Max(this.Scale(180f), ImGui.GetContentRegionAvail().Y);
        using (var list = ImRaii.Child("messages-list-card", new Vector2(-1f, listHeight), true))
        {
            if (!list.Success)
            {
                return;
            }
            ImGui.TextDisabled(this.activeMessageFolder switch
            {
                MessageFolder.Tickets => "Ticket Chats",
                MessageFolder.Staff => "Staff Chat",
                _ => "Recent Conversations",
            });
            var visibleConversations = this.GetVisibleMessageFolderConversations();
            if (visibleConversations.Count == 0)
            {
                ImGui.TextDisabled(this.activeMessageFolder switch
                {
                    MessageFolder.Tickets => "No ticket chats yet",
                    MessageFolder.Staff => "No staff chat yet",
                    _ => "No conversations yet",
                });
                if (this.activeMessageFolder == MessageFolder.Regular)
                {
                    ImGui.TextWrapped("Start a chat with any username or phone number above.");
                }
                else if (this.activeMessageFolder == MessageFolder.Tickets)
                {
                    ImGui.TextWrapped("Support tickets stay here so they do not clutter regular chats.");
                }
                else
                {
                    ImGui.TextWrapped("The staff room stays here so staff chatter stays separate.");
                }
                return;
            }
            foreach (var conversation in visibleConversations)
            {
                ImGui.TextUnformatted(conversation.DisplayName);
                if (conversation.UnreadCount > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.87f, 0.73f, 0.46f, 1f), $"[{conversation.UnreadCount}]");
                }
                ImGui.TextDisabled(conversation.LastMessagePreview);
                ImGui.TextDisabled($"{conversation.LastActivityUtc.LocalDateTime:t}  {(conversation.IsGroup ? "Group" : "Direct")}");
                if (ImGui.Button($"Open##{conversation.Id}", this.Scale(76f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.selectedConversationId = conversation.Id;
                    this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                    this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                    this.SyncMessageFolderForConversation(conversation.Id);
                    this.renderedMessageCount = 0;
                    this.scrollMessagesToBottom = true;
                    this.DismissNotificationsFor(conversation.Id);
                }
                ImGui.Separator();
            }
        }
    }
    private void DrawCalls()
    {
        this.TryAcknowledgeMissedCalls();

        using (var compose = ImRaii.Child("calls-compose-card", new Vector2(-1f, this.Scale(96f)), false))
        {
            if (compose.Success)
            {
                ImGui.TextDisabled("Start Call");
                var callButtonWidth = this.Scale(104f);
                var callInputWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - callButtonWidth - this.Scale(10f));
                ImGui.SetNextItemWidth(callInputWidth);
                ImGui.InputTextWithHint("##call-target", "Username or phone number", ref this.callTarget, 64);
                ImGui.SameLine();
                if (ImGui.Button("Call", new Vector2(callButtonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.callTarget))
                {
                    try
                    {
                        var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(this.callTarget)).GetAwaiter().GetResult();
                        this.BeginConversationCall(conversation.Id, false);
                        this.callTarget = string.Empty;
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                    }
                }
            }
        }

        var activeSectionHeight = this.activeCallSessions.Count > 0 ? this.Scale(148f) : this.Scale(64f);
        using (var active = ImRaii.Child("calls-active-card", new Vector2(-1f, activeSectionHeight), false))
        {
            if (active.Success)
            {
                ImGui.TextDisabled("Active Calls");
                if (this.activeCallSessions.Count == 0)
                {
                    ImGui.TextDisabled("No live calls right now");
                }
                else
                {
                    foreach (var session in this.activeCallSessions.OrderByDescending(item => item.StartedUtc))
                    {
                        ImGui.TextUnformatted(session.DisplayName);
                        ImGui.TextDisabled($"{(session.IsGroup ? "Group" : "Direct")} - {session.StartedUtc.LocalDateTime:g} - {(DateTimeOffset.UtcNow - session.StartedUtc):hh\\:mm\\:ss}");
                        var buttonLabel = session.IncludesCurrentAccount
                            ? (this.IsCurrentCallSession(session.Id) ? (session.IsGroup ? "Leave Call" : "End Call") : "Resume")
                            : "Join Call";
                        if (ImGui.Button($"{buttonLabel}##active-call-{session.Id}", this.Scale(112f, 28f)))
                        {
                            if (session.IncludesCurrentAccount && this.IsCurrentCallSession(session.Id))
                            {
                                this.LeaveCurrentCall();
                            }
                            else if (session.IncludesCurrentAccount)
                            {
                                this.state.ActiveCall = this.MapActiveCallState(session);
                                this.ConnectVoiceToCurrentCall();
                                this.pendingStatus = $"Resumed {session.DisplayName}";
                            }
                            else
                            {
                                this.BeginConversationCall(session.ConversationId, session.IsGroup);
                            }
                        }
                    }
                }
            }
        }

        using var history = ImRaii.Child("calls-history-card", new Vector2(-1f, 0f), true);
        if (!history.Success)
        {
            return;
        }
        ImGui.TextDisabled("Recent Calls");
        if (this.state.RecentCalls.Count == 0)
        {
            ImGui.TextDisabled("No calls yet");
            return;
        }
        foreach (var call in this.state.RecentCalls.OrderByDescending(item => item.StartedUtc))
        {
            using var item = ImRaii.Child($"call-{call.Id}", new Vector2(-1f, this.Scale(84f)), true);
            if (!item.Success)
            {
                continue;
            }
            ImGui.TextUnformatted(call.DisplayName);
            var directionLabel = call.Direction switch
            {
                CallDirection.Incoming => call.Missed ? "Received - Missed" : "Received",
                CallDirection.Outgoing => "Sent",
                CallDirection.Group => "Group",
                _ => "Unknown",
            };
            var durationLabel = call.Missed ? "No answer" : call.Duration.ToString(@"mm\:ss");
            ImGui.TextDisabled($"{directionLabel} - {durationLabel}");
            ImGui.TextDisabled(call.StartedUtc.LocalDateTime.ToString("g"));
        }
    }
    private void DrawContacts()
    {
        using (var add = ImRaii.Child("contacts-add-card", new Vector2(-1f, this.Scale(96f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (add.Success)
            {
                ImGui.TextDisabled("Add Contact");
                var contactButtonWidth = this.Scale(104f);
                var contactInputWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - contactButtonWidth - this.Scale(10f));
                ImGui.SetNextItemWidth(contactInputWidth);
                ImGui.InputTextWithHint("##contact-target", "Username or phone number", ref this.contactAddTarget, 64);
                ImGui.SameLine();
                if (ImGui.Button("Add", new Vector2(contactButtonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.contactAddTarget))
                {
                    try
                    {
                        var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(this.contactAddTarget)).GetAwaiter().GetResult();
                        var detail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                        var otherMember = detail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId);
                        if (otherMember is null)
                        {
                            this.pendingStatus = "Contact could not be resolved";
                        }
                        else
                        {
                            var contact = this.client.AddContactAsync(this.configuration.AuthToken, otherMember.AccountId, otherMember.DisplayName, otherMember.PhoneNumber).GetAwaiter().GetResult();
                            this.state.Contacts.RemoveAll(item => item.Id == contact.Id);
                            this.state.Contacts.Add(contact);
                            this.contactAddTarget = string.Empty;
                            this.pendingStatus = "Contact saved";
                        }
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = ex.Message;
                    }
                }
            }
        }

        using var contacts = ImRaii.Child("contacts-list-card", new Vector2(-1f, 0f), true);
        if (!contacts.Success)
        {
            return;
        }
        ImGui.TextDisabled("Contacts");
        if (this.state.Contacts.Count == 0)
        {
            ImGui.TextDisabled("No contacts saved");
            return;
        }
        foreach (var contact in this.state.Contacts.OrderBy(item => item.DisplayName))
        {
            using var item = ImRaii.Child($"contact-{contact.Id}", new Vector2(-1f, this.Scale(128f)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!item.Success)
            {
                continue;
            }

            this.DrawCopyableText(contact.DisplayName, contact.DisplayName, "Name copied");
            this.DrawCopyableText(contact.PhoneNumber, contact.PhoneNumber, "Phone number copied", true);
            if (!string.IsNullOrWhiteSpace(contact.Note))
            {
                this.DrawWrappedDisabledText(contact.Note);
            }
            var actionWidth = Math.Max(this.Scale(104f), (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f);
            if (ImGui.Button($"Message##{contact.Id}", new Vector2(actionWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(contact.PhoneNumber)).GetAwaiter().GetResult();
                this.selectedConversationId = conversation.Id;
                this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                this.showHomeScreen = false;
                this.activeTab = PhoneTab.Messages;
                this.scrollMessagesToBottom = true;
            }
            ImGui.SameLine();
            if (ImGui.Button($"Call##{contact.Id}", new Vector2(actionWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                try
                {
                    var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(contact.PhoneNumber)).GetAwaiter().GetResult();
                    this.BeginConversationCall(conversation.Id, false);
                }
                catch (Exception ex)
                {
                    this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                }
            }
        }
    }
    private void DrawFriends()
    {
        var splitSpacing = this.Scale(10f);
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var panelHeight = Math.Max(this.Scale(180f), (availableHeight - splitSpacing) * 0.5f);

        using (var request = ImRaii.Child("friends-request-card", new Vector2(-1f, panelHeight), true))
        {
            if (request.Success)
            {
                ImGui.TextDisabled("Send Friend Request");
                ImGui.InputTextWithHint("##friend-target", "Username or phone number", ref this.friendRequestTarget, 64);
                ImGui.InputTextWithHint("##friend-message", "Message", ref this.friendRequestMessage, 128);
                if (ImGui.Button("Send Request", new Vector2(-1f, this.Scale(34f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.friendRequestTarget))
                {
                    try
                    {
                        this.client.CreateFriendRequestAsync(this.configuration.AuthToken, new FriendRequestCreateRequest(this.friendRequestTarget, string.IsNullOrWhiteSpace(this.friendRequestMessage) ? null : this.friendRequestMessage)).GetAwaiter().GetResult();
                        this.friendRequestTarget = string.Empty;
                        this.friendRequestMessage = string.Empty;
                        this.pendingStatus = "Friend request sent";
                        this.RefreshSnapshot();
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                    }
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, splitSpacing));

        using var list = ImRaii.Child("friends-list-card", new Vector2(-1f, 0f), true);
        if (!list.Success)
        {
            return;
        }

        var pendingIncoming = this.state.FriendRequests.Where(item => item.Status == FriendRequestStatus.Pending && item.IsIncoming).OrderBy(item => item.DisplayName).ToList();
        var pendingOutgoing = this.state.FriendRequests.Where(item => item.Status == FriendRequestStatus.Pending && !item.IsIncoming).OrderBy(item => item.DisplayName).ToList();
        ImGui.TextDisabled("Friends");
        if (pendingIncoming.Count == 0 && pendingOutgoing.Count == 0 && this.state.Friends.Count == 0)
        {
            ImGui.TextDisabled("No friends or requests yet");
            return;
        }

        if (pendingIncoming.Count > 0)
        {
            ImGui.TextDisabled("Incoming Requests");
            foreach (var request in pendingIncoming)
            {
                using var item = ImRaii.Child($"friend-request-{request.Id}", new Vector2(-1f, this.Scale(106f)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                if (!item.Success)
                {
                    continue;
                }

                this.DrawCopyableText(request.DisplayName, request.DisplayName, "Name copied");
                this.DrawCopyableText($"Pending from {request.PhoneNumber}", request.PhoneNumber, "Phone number copied", true);
                var actionWidth = Math.Max(this.Scale(104f), (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f);
                if (ImGui.Button($"Accept##{request.Id}", new Vector2(actionWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    try
                    {
                        var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, true)).GetAwaiter().GetResult();
                        this.state.FriendRequests.RemoveAll(item => item.Id == request.Id);
                        if (updated is not null)
                        {
                            this.pendingStatus = "Friend added";
                            this.RefreshSnapshot();
                        }
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button($"Decline##{request.Id}", new Vector2(actionWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    try
                    {
                        var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, false)).GetAwaiter().GetResult();
                        this.state.FriendRequests.RemoveAll(item => item.Id == request.Id);
                        if (updated is not null)
                        {
                            this.pendingStatus = "Request declined";
                            this.RefreshSnapshot();
                        }
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                    }
                }
            }
        }

        if (pendingOutgoing.Count > 0)
        {
            if (pendingIncoming.Count > 0)
            {
                ImGui.Separator();
            }

            ImGui.TextDisabled("Sent Requests");
            foreach (var request in pendingOutgoing)
            {
                using var item = ImRaii.Child($"friend-request-outgoing-{request.Id}", new Vector2(-1f, this.Scale(86f)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                if (!item.Success)
                {
                    continue;
                }

                this.DrawCopyableText(request.DisplayName, request.DisplayName, "Name copied");
                this.DrawCopyableText($"Request sent to {request.PhoneNumber}", request.PhoneNumber, "Phone number copied", true);
            }
        }

        if ((pendingIncoming.Count > 0 || pendingOutgoing.Count > 0) && this.state.Friends.Count > 0)
        {
            ImGui.Separator();
        }

        foreach (var friend in this.state.Friends.OrderBy(item => item.FriendDisplayName))
        {
            using var item = ImRaii.Child($"friendship-{friend.FriendAccountId}", new Vector2(-1f, this.Scale(102f)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!item.Success)
            {
                continue;
            }
            this.DrawCopyableText(friend.FriendDisplayName, friend.FriendDisplayName, "Name copied");
            this.DrawCopyableText(friend.FriendPhoneNumber, friend.FriendPhoneNumber, "Phone number copied", true);
            this.DrawWrappedDisabledText($"Added {friend.SinceUtc.LocalDateTime:d}");
            if (ImGui.Button($"Remove##{friend.FriendAccountId}", new Vector2(-1f, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                try
                {
                    var removed = this.client.RemoveFriendAsync(this.configuration.AuthToken, friend.FriendAccountId).GetAwaiter().GetResult();
                    if (removed)
                    {
                        this.state.Friends.RemoveAll(item => item.FriendAccountId == friend.FriendAccountId);
                        this.pendingStatus = "Friend removed";
                        this.RefreshSnapshot();
                    }
                }
                catch (Exception ex)
                {
                    this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                }
            }
        }
    }
    private void DrawSessionRestoreScreen()
    {
        using var panel = ImRaii.Child("session-restore", new Vector2(-1f, -1f), false);
        if (!panel.Success)
        {
            return;
        }

        using var card = ImRaii.Child("session-restore-card", new Vector2(-1f, this.Scale(220f)), false);
        if (!card.Success)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(12f)));
        ImGui.TextDisabled("Restoring Account");
        var username = string.IsNullOrWhiteSpace(this.configuration.Username) ? "your account" : this.configuration.Username;
        ImGui.TextWrapped($"Signing back into {username} and loading your phone data.");
        ImGui.Spacing();
        ImGui.TextDisabled(this.pendingStatus);
        ImGui.Spacing();
        if (ImGui.Button("Retry Now", this.Scale(128f, 34f)))
        {
            this.refreshOnNextDraw = true;
            this.RefreshSnapshot();
        }
        ImGui.SameLine();
        if (ImGui.Button("Log Out", this.Scale(128f, 34f)))
        {
            this.SignOutToGuestState("Signed out");
        }
    }

    private void DrawSettings()
    {
        using var settingsScroll = ImRaii.Child("settings-scroll", new Vector2(-1f, 0f), true);
        if (!settingsScroll.Success)
        {
            return;
        }

        var isAuthenticated = !string.IsNullOrWhiteSpace(this.configuration.AuthToken);
        if (!isAuthenticated)
        {
            ImGui.TextDisabled("Account");
            ImGui.TextWrapped("Create an account or sign in to unlock the phone apps.");
            if (this.configuration.LocalAccountLockout)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
            }
            ImGui.InputTextWithHint("##settings-auth-user", "Username", ref this.loginUsername, 64);
            ImGui.InputTextWithHint("##settings-auth-pass", "Password", ref this.loginPassword, 64, ImGuiInputTextFlags.Password);
            var authButtonWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
            if (ImGui.Button("Create Account", new Vector2(authButtonWidth, this.Scale(34f))))
            {
                this.BeginRegister();
            }
            ImGui.SameLine();
            if (ImGui.Button("Sign In", new Vector2(authButtonWidth, this.Scale(34f))))
            {
                this.BeginLogin();
            }
            ImGui.Separator();
            ImGui.TextDisabled("Legal");
            if (ImGui.Button("Terms", new Vector2(-1f, this.Scale(30f))))
            {
                this.activeTab = PhoneTab.Legal;
                return;
            }
            if (ImGui.Button("Privacy", new Vector2(-1f, this.Scale(30f))))
            {
                this.activeTab = PhoneTab.Privacy;
                return;
            }
            this.SaveConfiguration();
            return;
        }

        ImGui.TextDisabled("Your Number");
        var phoneNumber = this.GetPhoneNumberForUi();
        if (ImGui.Button(phoneNumber, new Vector2(-1f, this.Scale(36f))))
        {
            ImGui.SetClipboardText(phoneNumber);
            this.pendingStatus = "Phone number copied";
        }
        ImGui.TextDisabled($"Username: {this.GetUsernameForUi()}");
        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        if (ImGui.Button("Terms", new Vector2(-1f, this.Scale(30f))))
        {
            this.activeTab = PhoneTab.Legal;
            return;
        }
        if (ImGui.Button("Privacy", new Vector2(-1f, this.Scale(30f))))
        {
            this.activeTab = PhoneTab.Privacy;
            return;
        }
        if (ImGui.Button("Log Out", new Vector2(-1f, this.Scale(30f))))
        {
            this.SignOutToGuestState("Signed out");
            return;
        }
        if (this.state.CurrentProfile.Role == AccountRole.User && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            if (ImGui.Button("Delete Account", new Vector2(-1f, this.Scale(30f))))
            {
                this.deleteAccountPassword = string.Empty;
                this.deleteAccountError = string.Empty;
                ImGui.OpenPopup("TomestonePhone Delete Account");
            }

            if (this.openDeleteAccountPasswordPopup)
            {
                this.openDeleteAccountPasswordPopup = false;
                ImGui.OpenPopup("TomestonePhone Confirm Delete Account");
            }

            if (ImGui.BeginPopupModal("TomestonePhone Delete Account", ImGuiWindowFlags.NoResize))
            {
                ImGui.SetWindowSize(new Vector2(this.Scale(320f), this.Scale(215f)), ImGuiCond.Appearing);
                ImGui.TextWrapped("Are you sure you want to delete your account?");
                ImGui.Spacing();
                ImGui.TextWrapped("This action is irreversible. Your account will be permanently deactivated and you will be logged out.");
                ImGui.Spacing();
                ImGui.TextWrapped("Your existing messages, call logs, and other history will still remain visible to other people.");
                ImGui.Spacing();
                if (ImGui.Button("Cancel", new Vector2(this.Scale(120f), this.Scale(32f))))
                {
                    this.deleteAccountPassword = string.Empty;
                    this.deleteAccountError = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Yes", new Vector2(this.Scale(120f), this.Scale(32f))))
                {
                    this.deleteAccountPassword = string.Empty;
                    this.deleteAccountError = string.Empty;
                    this.openDeleteAccountPasswordPopup = true;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("TomestonePhone Confirm Delete Account", ImGuiWindowFlags.NoResize))
            {
                ImGui.SetWindowSize(new Vector2(this.Scale(320f), this.Scale(195f)), ImGuiCond.Appearing);
                ImGui.TextWrapped("Enter your password to confirm account deletion.");
                ImGui.Spacing();
                ImGui.TextDisabled("Password");
                ImGui.InputText("##DeleteAccountPassword", ref this.deleteAccountPassword, 64, ImGuiInputTextFlags.Password);
                if (!string.IsNullOrWhiteSpace(this.deleteAccountError))
                {
                    ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), this.deleteAccountError);
                }
                ImGui.Spacing();
                if (ImGui.Button("Cancel", new Vector2(this.Scale(120f), this.Scale(32f))))
                {
                    this.deleteAccountPassword = string.Empty;
                    this.deleteAccountError = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Delete", new Vector2(this.Scale(120f), this.Scale(32f))))
                {
                    var success = this.client.DeleteAccountAsync(this.configuration.AuthToken!, new DeleteAccountRequest(this.deleteAccountPassword)).GetAwaiter().GetResult();
                    if (success)
                    {
                        this.deleteAccountPassword = string.Empty;
                        this.deleteAccountError = string.Empty;
                        ImGui.CloseCurrentPopup();
                        this.SignOutToGuestState("Account deleted");
                        return;
                    }

                    this.deleteAccountError = "Invalid password";
                }
                ImGui.EndPopup();
            }
        }


        ImGui.Separator();
        ImGui.TextDisabled("Appearance");
        this.DrawEditableText("Accent Color", this.configuration.AccentColorHex, value => this.configuration.AccentColorHex = value, 16);
        var lockViewport = this.configuration.LockViewport;
        if (ImGui.Checkbox("Lock viewport inside phone frame", ref lockViewport))
        {
            this.configuration.LockViewport = lockViewport;
        }
        this.DrawNotificationAnchorPicker();

        ImGui.Separator();
        ImGui.TextDisabled("Account");
        var playOpenEmote = this.configuration.PlayOpenEmote;
        if (ImGui.Checkbox("Play /tomestonephone emote when opening via command", ref playOpenEmote))
        {
            this.configuration.PlayOpenEmote = playOpenEmote;
        }
        var muted = this.state.CurrentProfile.NotificationsMuted;
        if (ImGui.Checkbox("Mute notifications", ref muted))
        {
            this.state.CurrentProfile = this.state.CurrentProfile with { NotificationsMuted = muted };
        }
        ImGui.TextDisabled("Current Password");
        ImGui.InputText("##CurrentPassword", ref this.oldPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.TextDisabled("New Password");
        ImGui.InputText("##NewPassword", ref this.newPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.TextDisabled("Confirm Password");
        ImGui.InputText("##ConfirmPassword", ref this.confirmPassword, 64, ImGuiInputTextFlags.Password);
        if (ImGui.Button("Change Password", new Vector2(-1f, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            if (this.newPassword != this.confirmPassword)
            {
                this.pendingStatus = "New passwords do not match";
            }
            else
            {
                var success = this.client.ChangePasswordAsync(this.configuration.AuthToken, new PasswordResetSelfRequest(this.oldPassword, this.newPassword, this.confirmPassword)).GetAwaiter().GetResult();
                this.pendingStatus = success ? "Password updated" : "Password update failed";
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Blocked Contacts");
        if (this.state.BlockedContacts.Count == 0)
        {
            ImGui.TextDisabled("No blocked contacts");
        }
        else
        {
            foreach (var blockedContact in this.state.BlockedContacts)
            {
                ImGui.TextUnformatted(blockedContact.DisplayName);
                if (ImGui.Button($"Unblock##{blockedContact.Id}", new Vector2(-1f, this.Scale(28f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var success = this.client.UnblockAccountAsync(this.configuration.AuthToken, blockedContact.Id).GetAwaiter().GetResult();
                    this.pendingStatus = success ? "Unblocked" : "Unblock failed";
                    if (success)
                    {
                        this.RefreshSnapshot();
                    }
                }
            }
        }

        this.SaveConfiguration();
    }
    private void SaveConfiguration()
    {
        this.service.PluginInterface.SavePluginConfig(this.configuration);
    }
    private void ImportBackgroundImage(string sourcePath)
    {
        var destinationPath = this.configuration.GetLocalWallpaperPath();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var image = Image.Load<Rgba32>(sourcePath);
        const int maxDimension = 1600;
        var scale = Math.Min(1f, Math.Min(maxDimension / (float)image.Width, maxDimension / (float)image.Height));
        if (scale < 1f)
        {
            var finalWidth = Math.Max(1, (int)MathF.Floor(image.Width * scale));
            var finalHeight = Math.Max(1, (int)MathF.Floor(image.Height * scale));
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(finalWidth, finalHeight),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3,
            }));
        }

        image.Save(destinationPath, new PngEncoder());
        this.appIconRenderer.Invalidate(this.configuration.BackgroundImagePath);
        this.configuration.BackgroundImagePath = destinationPath;
        this.configuration.BackgroundZoom = 1f;
        this.configuration.BackgroundOffsetX = 0f;
        this.configuration.BackgroundOffsetY = 0f;
        this.SaveConfiguration();
        this.pendingStatus = "Wallpaper updated";
    }

    private void ResetBackgroundImage()
    {
        this.appIconRenderer.Invalidate(this.configuration.BackgroundImagePath);
        this.configuration.BackgroundImagePath = string.Empty;
        this.configuration.BackgroundZoom = 1f;
        this.configuration.BackgroundOffsetX = 0f;
        this.configuration.BackgroundOffsetY = 0f;
        this.SaveConfiguration();
        this.pendingStatus = "Wallpaper reset";
    }

    private void DrawWallpaper(Vector2 screenMin, Vector2 screenMax, float rounding)
    {
        var texture = this.appIconRenderer.TryGetTexture(this.configuration.BackgroundImagePath);
        if (texture is null)
        {
            return;
        }

        var viewport = screenMax - screenMin;
        if (viewport.X <= 0f || viewport.Y <= 0f)
        {
            return;
        }

        var textureSize = new Vector2(texture.Width, texture.Height);
        if (textureSize.X <= 0f || textureSize.Y <= 0f)
        {
            return;
        }

        var viewportAspect = viewport.X / viewport.Y;
        var textureAspect = textureSize.X / textureSize.Y;
        var uvWidth = 1f;
        var uvHeight = 1f;
        if (textureAspect > viewportAspect)
        {
            uvWidth = viewportAspect / textureAspect;
        }
        else
        {
            uvHeight = textureAspect / viewportAspect;
        }

        var zoom = Math.Clamp(this.configuration.BackgroundZoom, 1f, 2.75f);
        uvWidth = Math.Clamp(uvWidth / zoom, 0.05f, 1f);
        uvHeight = Math.Clamp(uvHeight / zoom, 0.05f, 1f);
        var maxOffsetX = (1f - uvWidth) * 0.5f;
        var maxOffsetY = (1f - uvHeight) * 0.5f;
        var centerX = 0.5f + Math.Clamp(this.configuration.BackgroundOffsetX, -1f, 1f) * maxOffsetX;
        var centerY = 0.5f + Math.Clamp(this.configuration.BackgroundOffsetY, -1f, 1f) * maxOffsetY;
        var uv0 = new Vector2(centerX - uvWidth * 0.5f, centerY - uvHeight * 0.5f);
        var uv1 = new Vector2(centerX + uvWidth * 0.5f, centerY + uvHeight * 0.5f);

        ImGui.GetWindowDrawList().AddImageRounded(texture.Handle, screenMin, screenMax, uv0, uv1, ImGui.GetColorU32(Vector4.One), rounding);
    }
    private void RefreshStaffDashboard()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        this.adminDashboard = this.client.GetAdminDashboardAsync(this.configuration.AuthToken).GetAwaiter().GetResult();
    }

    private void OpenConversation(Guid conversationId, PhoneTab tab = PhoneTab.Messages)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        try
        {
            this.selectedConversationId = conversationId;
            this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId).GetAwaiter().GetResult();
            this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversationId).GetAwaiter().GetResult();
            this.SyncMessageFolderForConversation(conversationId);
            this.renderedMessageCount = 0;
            this.scrollMessagesToBottom = true;
            this.showHomeScreen = false;
            this.activeTab = tab;
            this.DismissNotificationsFor(conversationId);
        }
        catch (Exception ex)
        {
            this.selectedConversationId = null;
            this.selectedConversationMessages = null;
            this.selectedConversationDetail = null;
            this.pendingStatus = string.IsNullOrWhiteSpace(ex.Message) ? "Could not open conversation" : ex.Message;
            this.AnnounceDebugOnce($"Conversation open failed: {this.pendingStatus}", ex);
        }
    }

    private bool IsCurrentUserStaff()
    {
        return this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator;
    }

    private Guid? ResolveSingleConversationTarget(string rawTarget)
    {
        var resolved = this.ResolveConversationTargets(rawTarget).FirstOrDefault();
        return resolved == Guid.Empty ? null : resolved;
    }

    private void UpsertSupportTicket(SupportTicketRecord ticket)
    {
        this.state.SupportTickets.RemoveAll(item => item.Id == ticket.Id);
        this.state.SupportTickets.Insert(0, ticket);
        if (this.adminDashboard is not null)
        {
            var tickets = this.adminDashboard.Tickets.Where(item => item.Id != ticket.Id).Prepend(ticket).ToList();
            this.adminDashboard = new AdminDashboardSnapshot(this.adminDashboard.Accounts, this.adminDashboard.Reports, this.adminDashboard.AuditLogs, tickets, this.adminDashboard.ActiveAnnouncement);
        }
    }

    private void OpenStaffConversation()
    {
        var staffConversation = this.state.Conversations.FirstOrDefault(item => item.IsGroup && string.Equals(item.DisplayName, "Staff Room", StringComparison.OrdinalIgnoreCase));
        if (staffConversation is null)
        {
            this.RefreshSnapshot();
            staffConversation = this.state.Conversations.FirstOrDefault(item => item.IsGroup && string.Equals(item.DisplayName, "Staff Room", StringComparison.OrdinalIgnoreCase));
        }

        if (staffConversation is null)
        {
            this.pendingStatus = "Staff chat is not available yet";
            return;
        }

        this.activeMessageFolder = MessageFolder.Staff;
        this.OpenConversation(staffConversation.Id);
    }
    private ActiveCallState MapActiveCallState(ActiveCallSessionRecord session, bool isIncoming = false)
    {
        var existingMuted = this.state.ActiveCall?.SessionId == session.Id && this.state.ActiveCall.IsMuted;
        return new ActiveCallState
        {
            SessionId = session.Id,
            CallId = session.CallId,
            ConversationId = session.ConversationId,
            Title = session.DisplayName,
            Participants = session.Participants.ToList(),
            VoiceSession = session.VoiceSession,
            IsIncoming = isIncoming,
            IsMuted = existingMuted,
            IsGroup = session.IsGroup,
            StartedUtc = isIncoming ? DateTimeOffset.UtcNow : session.StartedUtc,
        };
    }

    private ActiveCallSessionRecord? GetConversationActiveCallSession(Guid conversationId)
    {
        return this.activeCallSessions.FirstOrDefault(item => item.ConversationId == conversationId);
    }

    private bool IsCurrentCallSession(Guid sessionId)
    {
        return this.state.ActiveCall?.SessionId == sessionId;
    }

    private void UpsertRecentCall(CallSummary summary)
    {
        this.state.RecentCalls.RemoveAll(item => item.Id == summary.Id);
        this.state.RecentCalls.Insert(0, summary);
    }

    private void LeaveCurrentCall(string? statusMessage = null)
    {
        var activeCall = this.state.ActiveCall;
        if (activeCall is null)
        {
            return;
        }

        var wasGroup = activeCall.IsGroup;
        try
        {
            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var summary = this.client.EndActiveCallAsync(this.configuration.AuthToken, activeCall.SessionId).GetAwaiter().GetResult();
                if (summary is not null)
                {
                    this.UpsertRecentCall(summary);
                }
            }
        }
        catch (Exception ex)
        {
            this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
        }

        this.voiceChatSession.StopAsync().GetAwaiter().GetResult();
        this.state.ActiveCall = null;
        this.DismissIncomingCallNotifications();
        this.lastActiveCallRefreshUtc = DateTimeOffset.MinValue;
        this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
        this.pendingStatus = statusMessage ?? (wasGroup ? "Left call" : "Call ended");
        this.RefreshSnapshot();
    }

    private void ConnectVoiceToCurrentCall()
    {
        var activeCall = this.state.ActiveCall;
        if (activeCall is null || activeCall.IsIncoming || activeCall.VoiceSession is null || string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        try
        {
            this.voiceChatSession.StartAsync(this.configuration.ServerBaseUrl, this.configuration.AuthToken, this.state.CurrentProfile.AccountId, activeCall).GetAwaiter().GetResult();
            this.voiceChatSession.SetMuted(activeCall.IsMuted);
        }
        catch (Exception ex)
        {
            this.pendingStatus = $"Voice unavailable: {this.SanitizeUserFacingError(ex.Message)}";
        }
    }

    private void TryAcknowledgeMissedCalls()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var missedCalls = this.state.RecentCalls.Where(item => item.Missed && !item.Acknowledged).ToList();
        if (missedCalls.Count == 0)
        {
            return;
        }

        try
        {
            var count = this.client.AcknowledgeMissedCallsAsync(this.configuration.AuthToken).GetAwaiter().GetResult();
            if (count <= 0)
            {
                return;
            }

            this.state.RecentCalls = this.state.RecentCalls
                .Select(item => item.Missed && !item.Acknowledged ? item with { Acknowledged = true } : item)
                .ToList();
            this.state.Notifications = this.state.Notifications
                .Where(item => item.Tab != PhoneTab.Calls || item.IsIncomingCall)
                .ToList();
        }
        catch (Exception ex)
        {
            this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
        }
    }
    private void BeginConversationCall(Guid conversationId, bool isGroup)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        try
        {
            if (this.state.ActiveCall is { ConversationId: var activeConversationId } && activeConversationId != conversationId)
            {
                this.LeaveCurrentCall("Switching calls...");
            }

            var session = this.client.StartOrJoinActiveCallAsync(this.configuration.AuthToken, new StartCallRequest(conversationId, isGroup)).GetAwaiter().GetResult();
            this.state.ActiveCall = this.MapActiveCallState(session);
            this.ConnectVoiceToCurrentCall();
            this.showHomeScreen = false;
            this.pendingStatus = session.IsGroup
                ? (session.IncludesCurrentAccount ? $"Joined {session.DisplayName}" : $"Call active in {session.DisplayName}")
                : $"Calling {session.DisplayName}";
            this.DismissNotificationsFor(conversationId);
            this.lastActiveCallRefreshUtc = DateTimeOffset.MinValue;
            this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
            if (this.pendingConversationMessagesTask is null && this.selectedConversationId == conversationId)
            {
                this.pendingConversationMessagesTask = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId);
            }
        }
        catch (Exception ex)
        {
            this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
        }
    }
    private void SendComposedMessage(Guid conversationId)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var body = this.composeMessage.Trim();
        var embedUrl = string.IsNullOrWhiteSpace(this.composeEmbedUrl) ? null : this.composeEmbedUrl.Trim();
        if (string.IsNullOrWhiteSpace(embedUrl) && this.gifEmbedRenderer.IsGifUrl(body))
        {
            embedUrl = body;
            body = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(embedUrl))
        {
            return;
        }

        var embeds = string.IsNullOrWhiteSpace(embedUrl)
            ? null
            : new List<SendMessageEmbedRequest> { new(embedUrl) };

        try
        {
            var sent = this.client.SendMessageAsync(this.configuration.AuthToken, new SendMessageRequest(conversationId, body, this.GetCurrentGameIdentity(), embeds)).GetAwaiter().GetResult();
            this.selectedConversationMessages = new ConversationMessagePage(conversationId, this.selectedConversationMessages?.Messages.Append(sent).ToList() ?? [sent]);
            this.composeMessage = string.Empty;
            this.composeEmbedUrl = string.Empty;
            this.composeControlVersion++;
            this.clearComposeOnNextDraw = true;
            this.focusComposeOnNextDraw = true;
            this.scrollMessagesToBottom = true;
            this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
            if (this.pendingConversationMessagesTask is null)
            {
                this.pendingConversationMessagesTask = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId);
            }
        }
        catch (Exception ex)
        {
            this.pendingStatus = ex.Message;
        }
    }

    private void SendGif(Guid conversationId, GiphyGifResult gif)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        try
        {
            var sent = this.client.SendMessageAsync(
                this.configuration.AuthToken,
                new SendMessageRequest(
                    conversationId,
                    string.Empty,
                    this.GetCurrentGameIdentity(),
                    [new SendMessageEmbedRequest(gif.GifUrl)])).GetAwaiter().GetResult();

            this.selectedConversationMessages = new ConversationMessagePage(conversationId, this.selectedConversationMessages?.Messages.Append(sent).ToList() ?? [sent]);
            this.scrollMessagesToBottom = true;
            this.pendingStatus = "GIF sent";
        }
        catch (Exception ex)
        {
            this.pendingStatus = ex.Message;
        }
    }

    private void DrawEditableText(string label, string value, Action<string> setter, int maxLength)
    {
        ImGui.TextDisabled(label);
        var buffer = value;
        if (ImGui.InputText($"##{label}", ref buffer, maxLength))
        {
            setter(buffer);
        }
    }

    private string SanitizeUserFacingError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "TomestonePhone error.";
        }

        var sanitized = message.Trim();
        if (!string.IsNullOrWhiteSpace(this.configuration.ServerBaseUrl))
        {
            sanitized = sanitized.Replace(this.configuration.ServerBaseUrl, "the server", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = Regex.Replace(sanitized, @"https?://[^\s\)]+", "the server", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d+)?\b", "the server", RegexOptions.IgnoreCase);
        return sanitized;
    }
    private void AnnounceDebugOnce(string message, Exception? ex = null)
    {
        var trimmed = this.SanitizeUserFacingError(message);
        if (string.Equals(this.lastChatDebugMessage, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        this.lastChatDebugMessage = trimmed;
        this.service.ChatGui.Print($"[TomestonePhone] {trimmed}");
        if (ex is not null)
        {
            this.service.Log.Warning(ex, trimmed);
        }
    }

    private void ClearDebugAnnouncement()
    {
        this.lastChatDebugMessage = null;
    }

    private void HandleServerAnnouncement(ServerAnnouncementRecord? announcement)
    {
        if (announcement is null)
        {
            return;
        }

        this.configuration.SeenAnnouncementIds ??= [];
        if (this.configuration.SeenAnnouncementIds.Contains(announcement.Id))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(announcement.Title) ? "Server Notice" : announcement.Title.Trim();
        var body = string.IsNullOrWhiteSpace(announcement.Body) ? "A server update notice was posted." : announcement.Body.Trim();
        this.service.ChatGui.Print($"[TomestonePhone Notice] {title}: {body}");
        this.configuration.SeenAnnouncementIds.Add(announcement.Id);
        this.SaveConfiguration();
    }
    private string GetUsernameForUi()
    {
        if (!string.IsNullOrWhiteSpace(this.state.CurrentProfile.Username) && !string.Equals(this.state.CurrentProfile.Username, "Guest", StringComparison.OrdinalIgnoreCase))
        {
            return this.state.CurrentProfile.Username;
        }

        return string.IsNullOrWhiteSpace(this.configuration.Username) ? "Guest" : this.configuration.Username!;
    }

    private string GetDisplayNameForUi()
    {
        if (!string.IsNullOrWhiteSpace(this.state.CurrentProfile.DisplayName) && !string.Equals(this.state.CurrentProfile.Username, "Guest", StringComparison.OrdinalIgnoreCase))
        {
            return this.state.CurrentProfile.DisplayName;
        }

        return this.GetUsernameForUi();
    }

    private string GetPhoneNumberForUi()
    {
        return string.IsNullOrWhiteSpace(this.state.CurrentProfile.PhoneNumber) ? "Unavailable" : this.state.CurrentProfile.PhoneNumber;
    }
    private void RefreshSnapshot(bool silent = false)
    {
        this.QueueSnapshotRefresh(silent);
    }

    private void QueueSnapshotRefresh(bool silent = false)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false })
        {
            if (!this.snapshotRefreshQueued)
            {
                this.snapshotRefreshQueuedSilently = silent;
            }
            else
            {
                this.snapshotRefreshQueuedSilently &= silent;
            }

            this.snapshotRefreshQueued = true;
            return;
        }

        var authToken = this.configuration.AuthToken!;
        var identity = this.GetCurrentGameIdentity();
        this.refreshOnNextDraw = false;
        this.snapshotRefreshQueued = false;
        this.snapshotRefreshQueuedSilently = false;
        if (!silent)
        {
            this.pendingStatus = "Refreshing account...";
        }

        this.pendingSnapshotTask = this.LoadPostAuthSnapshotAsync(authToken, identity);
    }

    private void TickSnapshotAutoRefresh()
    {
        if (!this.IsOpen || string.IsNullOrWhiteSpace(this.configuration.AuthToken) || this.showHomeScreen || this.activeTab == PhoneTab.Messages)
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false })
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var refreshInterval = this.activeTab is PhoneTab.Friends or PhoneTab.Contacts
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(6);
        if (now - this.lastSnapshotRefreshUtc < refreshInterval)
        {
            return;
        }

        this.lastSnapshotRefreshUtc = now;
        this.RefreshSnapshot(true);
    }

    private void TickActiveCallAutoRefresh()
    {
        if (!this.IsOpen || string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false } || this.pendingActiveCallsTask is { IsCompleted: false })
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - this.lastActiveCallRefreshUtc < TimeSpan.FromSeconds(5))
        {
            return;
        }

        this.lastActiveCallRefreshUtc = now;
        this.pendingActiveCallsTask = this.client.GetActiveCallsAsync(this.configuration.AuthToken!);
    }

    private void ProcessActiveCallSessions(IReadOnlyList<ActiveCallSessionRecord> sessions)
    {
        this.activeCallSessions = sessions.ToList();

        if (this.state.ActiveCall is { SessionId: var activeSessionId } && sessions.FirstOrDefault(item => item.Id == activeSessionId) is { } current)
        {
            this.state.ActiveCall = this.MapActiveCallState(current, this.state.ActiveCall.IsIncoming);
            this.voiceChatSession.SetMuted(this.state.ActiveCall.IsMuted);
            if (!this.state.ActiveCall.IsIncoming && !this.voiceChatSession.IsConnected)
            {
                this.ConnectVoiceToCurrentCall();
            }
        }
        else if (this.state.ActiveCall is not null && sessions.All(item => item.Id != this.state.ActiveCall.SessionId))
        {
            this.voiceChatSession.StopAsync().GetAwaiter().GetResult();
            this.state.ActiveCall = null;
        }

        var currentAccountId = this.state.CurrentProfile.AccountId;
        foreach (var session in sessions)
        {
            if (session.IsGroup || session.StartedByAccountId == currentAccountId || this.seenIncomingDirectCallSessionIds.Contains(session.Id))
            {
                continue;
            }

            this.seenIncomingDirectCallSessionIds.Add(session.Id);
            if (this.state.CurrentProfile.PresenceStatus == PhonePresenceStatus.DoNotDisturb || this.state.CurrentProfile.NotificationsMuted)
            {
                continue;
            }

            this.state.ActiveCall = this.MapActiveCallState(session, true);
            this.state.Notifications.Add(new PhoneNotification(Guid.NewGuid(), "Incoming Call", $"{session.DisplayName} is calling", PhoneTab.Calls, session.ConversationId, true));
        }

        var endedIncoming = this.seenIncomingDirectCallSessionIds.Where(id => sessions.All(item => item.Id != id)).ToList();
        foreach (var sessionId in endedIncoming)
        {
            this.seenIncomingDirectCallSessionIds.Remove(sessionId);
            if (this.state.ActiveCall is { IsIncoming: true, SessionId: var incomingSessionId } && incomingSessionId == sessionId)
            {
                var previousCall = this.state.ActiveCall;
                this.state.ActiveCall = null;
                if (previousCall is not null)
                {
                    this.state.Notifications.Add(new PhoneNotification(Guid.NewGuid(), "Missed Call", $"Missed call from {previousCall.Title}", PhoneTab.Calls, previousCall.ConversationId, false));
                }
            }
        }
    }

    private void HandleAuthFailure(Exception ex)
    {
        if (ex is ClientUpgradeRequiredException upgradeRequired)
        {
            this.ApplyClientUpgradeRequired(upgradeRequired.MinimumVersion, upgradeRequired.UpdateMessage);
            return;
        }

        var message = ex.ToString();
        if (message.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase) && this.autoLoginAttempted)
        {
            this.configuration.ClearRememberedCredentials();
            this.SaveConfiguration();
        }

        if (message.Contains("403") || message.Contains("banned", StringComparison.OrdinalIgnoreCase) || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            this.configuration.LocalAccountLockout = true;
            this.configuration.LocalAccountLockoutReason = "This device is locked due to a banned account or IP restriction.";
            this.configuration.AuthToken = null;
            this.configuration.Username = null;
            this.configuration.ClearRememberedCredentials();
            this.SaveConfiguration();
            this.pendingStatus = "Device locked";
            return;
        }

        this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(ex.Message) ? "Authentication failed" : ex.Message);
        this.AnnounceDebugOnce($"Auth failure: {this.pendingStatus}", ex);
    }
    private void ApplyClientUpgradeRequired(string minimumVersion, string updateMessage)
    {
        this.clientVersionChecked = true;
        this.clientUpdateRequired = true;
        this.minimumClientVersion = minimumVersion ?? string.Empty;
        this.clientUpdateMessage = string.IsNullOrWhiteSpace(updateMessage)
            ? "Please update TomestonePhone to the latest version before using the app."
            : updateMessage;
        this.pendingStatus = "Update required";
        this.SignOutToGuestState(this.pendingStatus, false, false, false);
        this.AnnounceClientUpdateRequiredOnce();
    }

    private void AnnounceClientUpdateRequiredOnce()
    {
        if (this.clientUpdateNoticeShown)
        {
            return;
        }

        this.clientUpdateNoticeShown = true;
        var message = string.IsNullOrWhiteSpace(this.clientUpdateMessage)
            ? "Please update TomestonePhone to the latest version before using the app."
            : this.clientUpdateMessage;
        this.service.ChatGui.Print($"[TomestonePhone] {message}");
    }
    private void AnnounceRecommendedVersionOnce()
    {
        if (this.clientRecommendedNoticeShown)
        {
            return;
        }

        this.clientRecommendedNoticeShown = true;
        var message = string.IsNullOrWhiteSpace(this.clientRecommendedMessage)
            ? "A newer TomestonePhone version is available. Please update soon because older versions may stop working."
            : this.clientRecommendedMessage;
        this.service.ChatGui.Print($"[TomestonePhone] {message}");
    }

    private void DrawHomeButton()
    {
        var available = ImGui.GetContentRegionAvail();
        var hitSize = new Vector2(this.Scale(176f), this.Scale(28f));
        var visualSize = new Vector2(this.Scale(132f), this.Scale(9f));
        var cursor = new Vector2(Math.Max(0f, (available.X - hitSize.X) * 0.5f), Math.Max(0f, (available.Y - hitSize.Y) * 0.5f));
        ImGui.SetCursorPos(cursor);
        using var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, hitSize.Y * 0.5f);
        using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.01f));
        using var buttonHovered = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.06f));
        using var buttonActive = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.1f));
        var hitPos = ImGui.GetCursorScreenPos();
        if (ImGui.Button("##Home", hitSize))
        {
            this.selectedConversationId = null;
            this.selectedConversationMessages = null;
            this.selectedConversationDetail = null;

            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.showHomeScreen = true;
                this.activeTab = PhoneTab.Messages;
                if (this.pendingSnapshotTask is not { IsCompleted: false } && this.HasHydratedAuthenticatedProfile())
                {
                    this.pendingStatus = $"Synced {DateTime.Now:t}";
                }
            }
            else
            {
                this.showHomeScreen = false;
                this.activeTab = PhoneTab.Settings;
                this.refreshOnNextDraw = false;
            }
        }

        var visualPos = new Vector2(hitPos.X + (hitSize.X - visualSize.X) * 0.5f, hitPos.Y + (hitSize.Y - visualSize.Y) * 0.5f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(visualPos + new Vector2(0f, this.Scale(2f)), visualPos + visualSize + new Vector2(0f, this.Scale(2f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.2f)), 999f);
        draw.AddRectFilled(visualPos, visualPos + visualSize, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.78f)), 999f);
    }

    private void DrawNotifications()
    {
        if (this.state.Notifications.Count == 0)
        {
            return;
        }

        var viewport = ImGui.GetMainViewport();
        var windowSize = new Vector2(280f, 96f);
        var anchorPos = this.configuration.NotificationAnchor switch
        {
            NotificationAnchor.TopLeft => viewport.Pos + new Vector2(18f, 18f),
            NotificationAnchor.TopRight => viewport.Pos + new Vector2(viewport.Size.X - windowSize.X - 18f, 18f),
            NotificationAnchor.BottomLeft => viewport.Pos + new Vector2(18f, viewport.Size.Y - windowSize.Y - 18f),
            _ => viewport.Pos + new Vector2(viewport.Size.X - windowSize.X - 18f, viewport.Size.Y - windowSize.Y - 18f),
        };

        var notification = this.state.Notifications[0];
        ImGui.SetNextWindowBgAlpha(0.92f);
        ImGui.SetNextWindowPos(anchorPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);

        if (ImGui.Begin("TomestonePhoneNotification", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
        {
            ImGui.TextUnformatted(notification.Title);
            ImGui.TextWrapped(notification.Body);

            if (ImGui.Button("Open", new Vector2(90f, 26f)))
            {
                this.IsOpen = true;
                this.showHomeScreen = false;
                this.activeTab = notification.Tab;
                if (notification.IsIncomingCall && this.state.ActiveCall is not null)
                {
                    this.BeginConversationCall(this.state.ActiveCall.ConversationId, this.state.ActiveCall.IsGroup);
                }

                this.state.Notifications.RemoveAt(0);
            }

            ImGui.SameLine();

            if (ImGui.Button("Dismiss", new Vector2(90f, 26f)))
            {
                this.state.Notifications.RemoveAt(0);
            }
        }

        ImGui.End();
    }

    private void DrawCallBanner()
    {
        if (this.state.ActiveCall is null)
        {
            return;
        }

        var call = this.state.ActiveCall;
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(this.Scale(320f, 286f), ImGuiCond.Always);
        var open = true;
        if (!ImGui.Begin("Call###TomestonePhoneCallPopup", ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        if (!open)
        {
            this.LeaveCurrentCall();
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(call.IsIncoming ? $"Incoming Call: {call.Title}" : call.Title);
        var elapsed = call.IsIncoming ? "Ringing..." : (DateTimeOffset.UtcNow - call.StartedUtc).ToString(@"hh\:mm\:ss");
        ImGui.TextDisabled(elapsed);
        if (call.VoiceSession is not null)
        {
            ImGui.TextDisabled(call.VoiceSession.QualityLabel);
        }

        using (var participantList = ImRaii.Child("call-popup-participants", new Vector2(-1f, this.Scale(148f)), true))
        {
            if (participantList.Success)
            {
                ImGui.TextDisabled("Participants");
                foreach (var participant in call.Participants)
                {
                    ImGui.BulletText(participant);
                }
            }
        }

        if (call.IsIncoming)
        {
            var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(10f)) * 0.5f;
            if (ImGui.Button("Accept", new Vector2(actionWidth, this.Scale(34f))))
            {
                this.BeginConversationCall(call.ConversationId, call.IsGroup);
                ImGui.End();
                return;
            }
            ImGui.SameLine();
            if (ImGui.Button("Decline", new Vector2(actionWidth, this.Scale(34f))))
            {
                this.DismissIncomingCallNotifications();
                this.state.ActiveCall = null;
                this.pendingStatus = "Call dismissed";
            }
        }
        else
        {
            var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(10f)) * 0.5f;
            var muteLabel = call.IsMuted ? "Unmute" : "Mute";
            if (ImGui.Button(muteLabel, new Vector2(actionWidth, this.Scale(34f))))
            {
                call.IsMuted = !call.IsMuted;
                this.voiceChatSession.SetMuted(call.IsMuted);
            }
            ImGui.SameLine();
            if (ImGui.Button(call.IsGroup ? "Leave Call" : "End Call", new Vector2(actionWidth, this.Scale(34f))))
            {
                this.LeaveCurrentCall();
            }
        }

        ImGui.End();
    }

    private void DrawCopyableText(string text, string copiedValue, string copiedStatus, bool disabled = false)
    {
        ImGui.PushTextWrapPos(0f);
        if (disabled)
        {
            ImGui.TextDisabled(text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }

        ImGui.PopTextWrapPos();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to copy");
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(copiedValue);
                this.pendingStatus = copiedStatus;
            }
        }
    }

    private void DrawWrappedDisabledText(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }
    private void DrawNotificationAnchorPicker()
    {
        ImGui.TextDisabled("Notification Spot");
        var anchor = this.configuration.NotificationAnchor;
        if (ImGui.BeginCombo("##NotificationSpot", anchor.ToString()))
        {
            foreach (NotificationAnchor value in Enum.GetValues(typeof(NotificationAnchor)))
            {
                var selected = value == anchor;
                if (ImGui.Selectable(value.ToString(), selected))
                {
                    this.configuration.NotificationAnchor = value;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DismissNotificationsFor(Guid conversationId)
    {
        this.state.Notifications = this.state.Notifications
            .Where(item => item.TargetId != conversationId)
            .ToList();
    }

    private void DismissIncomingCallNotifications()
    {
        this.state.Notifications = this.state.Notifications
            .Where(item => !item.IsIncomingCall)
            .ToList();
    }

    private void EnforceAspectRatio()
    {
        var currentSize = ImGui.GetWindowSize();
        if (currentSize.X <= 0f || currentSize.Y <= 0f)
        {
            return;
        }

        var minimumSize = new Vector2(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
        var maximumSize = new Vector2(DefaultWindowWidth * MaximumWindowScale, DefaultWindowHeight * MaximumWindowScale);

        var widthChanged = Math.Abs(currentSize.X - this.lastWindowSize.X) >= Math.Abs(currentSize.Y - this.lastWindowSize.Y);
        var corrected = widthChanged
            ? new Vector2(currentSize.X, currentSize.X / PhoneAspectRatio)
            : new Vector2(currentSize.Y * PhoneAspectRatio, currentSize.Y);

        corrected.X = Math.Clamp(corrected.X, minimumSize.X, maximumSize.X);
        corrected.Y = Math.Clamp(corrected.Y, minimumSize.Y, maximumSize.Y);

        if (Math.Abs(corrected.X - currentSize.X) > 0.5f || Math.Abs(corrected.Y - currentSize.Y) > 0.5f)
        {
            ImGui.SetWindowSize(corrected);
        }

        this.lastWindowSize = corrected;
    }

    private void DrawPhoneShell()
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var shellColor = ImGui.GetColorU32(new Vector4(0.055f, 0.065f, 0.09f, 1f));
        var trimColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
        var screenMin = windowPos + new Vector2(this.Scale(8f), this.Scale(8f));
        var screenMax = windowPos + windowSize - new Vector2(this.Scale(8f), this.Scale(8f));

        drawList.AddRectFilled(windowPos, windowPos + windowSize, shellColor, this.Scale(42f));
        drawList.AddRect(windowPos, windowPos + windowSize, trimColor, this.Scale(42f), ImDrawFlags.None, 1.4f);
        var screenRounding = this.Scale(36f);
        this.DrawWallpaper(screenMin, screenMax, screenRounding);
        drawList.AddRectFilledMultiColor(
            screenMin,
            screenMax,
            ImGui.GetColorU32(new Vector4(0.14f, 0.16f, 0.34f, 0.44f)),
            ImGui.GetColorU32(new Vector4(0.19f, 0.14f, 0.36f, 0.4f)),
            ImGui.GetColorU32(new Vector4(0.03f, 0.08f, 0.18f, 0.52f)),
            ImGui.GetColorU32(new Vector4(0.04f, 0.11f, 0.18f, 0.48f)));
        drawList.AddRect(screenMin, screenMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), screenRounding, ImDrawFlags.None, 1f);
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.76f, windowSize.Y * 0.2f), windowSize.X * 0.45f, ImGui.GetColorU32(new Vector4(0.98f, 0.72f, 0.42f, 0.08f)), 80);
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.18f, windowSize.Y * 0.58f), windowSize.X * 0.34f, ImGui.GetColorU32(new Vector4(0.27f, 0.82f, 0.96f, 0.06f)), 80);
        drawList.AddRectFilled(screenMin + new Vector2(0f, this.Scale(12f)), screenMax - new Vector2(0f, windowSize.Y * 0.68f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.015f)), this.Scale(28f));


    }

    private void DrawTopNotchOverlay()
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var islandWidth = windowSize.X * 0.31f;
        var islandHeight = this.Scale(30f);
        var islandMin = new Vector2(windowPos.X + (windowSize.X - islandWidth) * 0.5f, windowPos.Y + this.Scale(10f));
        var islandMax = islandMin + new Vector2(islandWidth, islandHeight);
        drawList.AddRectFilled(islandMin + new Vector2(0f, this.Scale(3f)), islandMax + new Vector2(0f, this.Scale(3f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), this.Scale(16f));
        drawList.AddRectFilled(islandMin, islandMax, ImGui.GetColorU32(new Vector4(0.02f, 0.03f, 0.04f, 1f)), this.Scale(16f));
        var speakerMin = islandMin + new Vector2(islandWidth * 0.26f, islandHeight * 0.45f);
        var speakerMax = speakerMin + new Vector2(islandWidth * 0.48f, this.Scale(4f));
        drawList.AddRectFilled(speakerMin, speakerMax, ImGui.GetColorU32(new Vector4(0.22f, 0.25f, 0.29f, 1f)), this.Scale(4f));
    }
    private void DrawLegalApp()
    {
        ImGui.TextDisabled($"Terms Version: {LegalTerms.Version}");
        if (this.configuration.AcceptedLegalTermsAtUtc is { } acceptedAt)
        {
            ImGui.TextDisabled($"Accepted on this computer: {acceptedAt.LocalDateTime:g}");
        }

        ImGui.Separator();
        ImGui.TextWrapped(LegalTerms.Summary);
        ImGui.Separator();
        using var scroll = ImRaii.Child("legal-app-scroll", new Vector2(0f, 0f), true);
        if (scroll.Success)
        {
            ImGui.TextWrapped(LegalTerms.FullText);
        }
    }

    private void DrawPrivacyApp()
    {
        ImGui.TextDisabled($"Privacy Version: {PrivacyPolicy.Version}");
        if (this.configuration.AcceptedPrivacyPolicyAtUtc is { } acceptedAt)
        {
            ImGui.TextDisabled($"Accepted on this computer: {acceptedAt.LocalDateTime:g}");
        }

        ImGui.Separator();
        ImGui.TextWrapped(PrivacyPolicy.Summary);
        ImGui.Separator();
        using var scroll = ImRaii.Child("privacy-app-scroll", new Vector2(0f, 0f), true);
        if (scroll.Success)
        {
            ImGui.TextWrapped(PrivacyPolicy.FullText);
        }
    }

    private void DrawSupportApp()
    {
        using (var compose = ImRaii.Child("support-compose-card", new Vector2(-1f, this.Scale(274f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (compose.Success)
            {
                ImGui.TextDisabled("Support");
                ImGui.TextWrapped("Open a support ticket to start a help chat with staff. Your ticket stays readable after staff close it.");
                ImGui.TextDisabled("Subject");
                ImGui.InputText("##support-subject", ref this.supportSubject, 96);
                ImGui.TextDisabled("What do you need help with?");
                ImGui.InputTextMultiline("##support-body", ref this.supportBody, 512, new Vector2(-1f, this.Scale(108f)));
                if (ImGui.Button("Open Support Ticket", new Vector2(this.Scale(176f), this.Scale(34f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    try
                    {
                        var ticket = this.client.CreateSupportTicketAsync(this.configuration.AuthToken, new CreateSupportTicketRequest(this.supportSubject, this.supportBody, false)).GetAwaiter().GetResult();
                        this.UpsertSupportTicket(ticket);
                        this.supportSubject = string.Empty;
                        this.supportBody = string.Empty;
                        this.RefreshSnapshot();
                        this.OpenConversation(ticket.ConversationId, PhoneTab.Support);
                        this.pendingStatus = "Support ticket opened";
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                    }
                }
            }
        }

        using (var tickets = ImRaii.Child("support-ticket-list", new Vector2(-1f, 0f), true))
        {
            if (!tickets.Success)
            {
                return;
            }

            ImGui.TextDisabled("Your Tickets");
            if (this.state.SupportTickets.Count == 0)
            {
                ImGui.TextDisabled("No support tickets yet");
                return;
            }

            foreach (var ticket in this.state.SupportTickets.OrderByDescending(item => item.CreatedAtUtc))
            {
                using var item = ImRaii.Child($"ticket-{ticket.Id}", new Vector2(-1f, this.Scale(100f)), false);
                if (!item.Success)
                {
                    continue;
                }

                ImGui.TextUnformatted(ticket.Subject);
                ImGui.TextDisabled($"{ticket.Status}  {ticket.CreatedAtUtc.LocalDateTime:g}");
                if (!string.IsNullOrWhiteSpace(ticket.Body))
                {
                    ImGui.TextWrapped(ticket.Body);
                }
                if (ImGui.Button($"Open Chat##support-open-{ticket.Id}", new Vector2(this.Scale(132f), this.Scale(30f))))
                {
                    this.OpenConversation(ticket.ConversationId, PhoneTab.Support);
                }
            }
        }
    }

    private void DrawStaffApp()
    {
        if (!this.IsCurrentUserStaff())
        {
            ImGui.TextDisabled("Staff access only.");
            return;
        }

        var dashboard = this.adminDashboard;
        var topHeight = this.Scale(dashboard is null ? 164f : 204f);
        using (var summary = ImRaii.Child("staff-summary-card", new Vector2(-1f, topHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (summary.Success)
            {
                ImGui.TextDisabled("Staff Console");
                ImGui.TextWrapped("Use staff chat, manage support tickets, and review accounts from one place.");
                ImGui.Spacing();

                if (ImGui.Button("Refresh Staff Data", this.Scale(176f, 34f)))
                {
                    this.RefreshStaffDashboard();
                    this.RefreshSnapshot();
                }
                ImGui.SameLine();
                if (ImGui.Button("Open Staff Chat", this.Scale(164f, 34f)))
                {
                    this.OpenStaffConversation();
                }

                if (dashboard is not null)
                {
                    ImGui.Spacing();
                    ImGui.TextDisabled($"Online now: {dashboard.Accounts.Count(account => account.IsOnline)} of {dashboard.Accounts.Count}");
                    ImGui.TextDisabled($"Open tickets: {dashboard.Tickets.Count(ticket => ticket.Status == SupportTicketStatus.Open)}");
                }
            }
        }

        using var body = ImRaii.Child("staff-body-scroll", new Vector2(-1f, 0f), true);
        if (!body.Success)
        {
            return;
        }

        if (dashboard is null)
        {
            ImGui.TextDisabled("Refresh the staff console to load staff data.");
            return;
        }

        ImGui.TextDisabled("Support Tickets");
        if (dashboard.Tickets.Count == 0)
        {
            ImGui.TextDisabled("No support tickets.");
        }
        else
        {
            foreach (var ticket in dashboard.Tickets.OrderByDescending(item => item.CreatedAtUtc))
            {
                using var ticketRow = ImRaii.Child($"staff-ticket-{ticket.Id}", new Vector2(-1f, this.Scale(124f)), false);
                if (!ticketRow.Success)
                {
                    continue;
                }

                ImGui.TextUnformatted(ticket.Subject);
                ImGui.TextDisabled($"{ticket.OwnerDisplayName}  {ticket.Status}  {ticket.CreatedAtUtc.LocalDateTime:g}");
                if (!string.IsNullOrWhiteSpace(ticket.Body))
                {
                    ImGui.TextWrapped(ticket.Body);
                }

                if (ImGui.Button($"Open Chat##staff-open-ticket-{ticket.Id}", this.Scale(114f, 28f)))
                {
                    this.OpenConversation(ticket.ConversationId, PhoneTab.Staff);
                }

                if (ticket.Status == SupportTicketStatus.Open)
                {
                    ImGui.SameLine();
                    var addWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - this.Scale(214f));
                    ImGui.SetNextItemWidth(addWidth);
                    ImGui.InputTextWithHint($"##staff-ticket-add-{ticket.Id}", "Add participant", ref this.staffTicketParticipantTarget, 64);
                    ImGui.SameLine();
                    if (ImGui.Button($"Add##staff-ticket-add-btn-{ticket.Id}", this.Scale(42f, 28f)))
                    {
                        try
                        {
                            var targetAccountId = this.ResolveSingleConversationTarget(this.staffTicketParticipantTarget);
                            if (targetAccountId is null)
                            {
                                this.pendingStatus = "Person could not be resolved";
                            }
                            else
                            {
                                var updated = this.client.AddSupportTicketParticipantAsync(this.configuration.AuthToken!, ticket.Id, targetAccountId.Value).GetAwaiter().GetResult();
                                if (updated is null)
                                {
                                    this.pendingStatus = "Could not add participant";
                                }
                                else
                                {
                                    this.UpsertSupportTicket(updated);
                                    this.staffTicketParticipantTarget = string.Empty;
                                    this.RefreshSnapshot();
                                    this.RefreshStaffDashboard();
                                    this.pendingStatus = "Participant added";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            this.pendingStatus = ex.Message;
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Close##staff-ticket-close-{ticket.Id}", this.Scale(52f, 28f)))
                    {
                        try
                        {
                            var updated = this.client.CloseSupportTicketAsync(this.configuration.AuthToken!, ticket.Id).GetAwaiter().GetResult();
                            if (updated is null)
                            {
                                this.pendingStatus = "Could not close ticket";
                            }
                            else
                            {
                                this.UpsertSupportTicket(updated);
                                this.RefreshSnapshot();
                                this.RefreshStaffDashboard();
                                this.pendingStatus = "Ticket closed";
                            }
                        }
                        catch (Exception ex)
                        {
                            this.pendingStatus = ex.Message;
                        }
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("Accounts");
        ImGui.InputTextWithHint("##staff-search", "Search username, display, or phone", ref this.staffSearchQuery, 64);
        var accounts = dashboard.Accounts
            .Where(account => string.IsNullOrWhiteSpace(this.staffSearchQuery)
                || account.Username.Contains(this.staffSearchQuery, StringComparison.OrdinalIgnoreCase)
                || account.DisplayName.Contains(this.staffSearchQuery, StringComparison.OrdinalIgnoreCase)
                || account.PhoneNumber.Contains(this.staffSearchQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(account => account.IsOnline)
            .ThenBy(account => account.Role)
            .ThenBy(account => account.Username)
            .ToList();

        if (accounts.Count == 0)
        {
            ImGui.TextDisabled("No matching accounts.");
        }

        foreach (var account in accounts)
        {
            using var item = ImRaii.Child($"staff-account-{account.AccountId}", new Vector2(-1f, this.Scale(156f)), false);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted($"{account.DisplayName} (@{account.Username})");
            var onlineLabel = account.IsOnline ? "Online now" : $"Last seen {account.LastSeenAtUtc?.LocalDateTime:g}";
            ImGui.TextDisabled($"{account.Role}  {account.Status}  {account.PhoneNumber}  {onlineLabel}");
            if (account.KnownIpAddresses.Count > 0)
            {
                ImGui.TextDisabled(string.Join(", ", account.KnownIpAddresses));
            }

            if (this.state.CurrentProfile.Role == AccountRole.Owner && account.Role != AccountRole.Owner && account.AccountId != this.state.CurrentProfile.AccountId)
            {
                var roleWidth = (ImGui.GetContentRegionAvail().X - this.Scale(16f)) / 3f;
                if (ImGui.Button($"User##role-user-{account.AccountId}", new Vector2(roleWidth, this.Scale(28f))))
                {
                    this.client.UpdateAccountRoleAsync(this.configuration.AuthToken!, new UpdateAccountRoleRequest(account.AccountId, AccountRole.User)).GetAwaiter().GetResult();
                    this.RefreshSnapshot();
                    this.RefreshStaffDashboard();
                    this.pendingStatus = $"{account.Username} is now User";
                }
                ImGui.SameLine();
                if (ImGui.Button($"Moderator##role-mod-{account.AccountId}", new Vector2(roleWidth, this.Scale(28f))))
                {
                    this.client.UpdateAccountRoleAsync(this.configuration.AuthToken!, new UpdateAccountRoleRequest(account.AccountId, AccountRole.Moderator)).GetAwaiter().GetResult();
                    this.RefreshSnapshot();
                    this.RefreshStaffDashboard();
                    this.pendingStatus = $"{account.Username} is now Moderator";
                }
                ImGui.SameLine();
                if (ImGui.Button($"Admin##role-admin-{account.AccountId}", new Vector2(roleWidth, this.Scale(28f))))
                {
                    this.client.UpdateAccountRoleAsync(this.configuration.AuthToken!, new UpdateAccountRoleRequest(account.AccountId, AccountRole.Admin)).GetAwaiter().GetResult();
                    this.RefreshSnapshot();
                    this.RefreshStaffDashboard();
                    this.pendingStatus = $"{account.Username} is now Admin";
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("Reports");
        if (dashboard.Reports.Count == 0)
        {
            ImGui.TextDisabled("No open reports.");
        }
        else
        {
            foreach (var report in dashboard.Reports.OrderByDescending(item => item.CreatedAtUtc).Take(4))
            {
                ImGui.TextWrapped($"{report.Category} [{report.Status}]  {report.ReporterDisplayName}");
                ImGui.TextDisabled(report.Reason);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("Audit Logs");
        foreach (var log in dashboard.AuditLogs.OrderByDescending(item => item.CreatedAtUtc).Take(4))
        {
            ImGui.TextWrapped($"{log.CreatedAtUtc.LocalDateTime:g}  {log.EventType}  {log.Summary}");
        }

        if (this.state.CurrentProfile.Role == AccountRole.Owner)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextDisabled("Owner Password Reset");
            ImGui.TextDisabled("Target Account Id");
            ImGui.InputText("##owner-reset-target", ref this.ownerResetTarget, 64);
            ImGui.TextDisabled("New Owner Password");
            ImGui.InputText("##owner-reset-password", ref this.ownerResetPassword, 64, ImGuiInputTextFlags.Password);
            if (ImGui.Button("Reset Account Password", this.Scale(184f, 34f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && Guid.TryParse(this.ownerResetTarget, out var targetAccountId))
            {
                var success = this.client.ResetPasswordAsOwnerAsync(this.configuration.AuthToken, new AdminPasswordResetRequest(targetAccountId, this.ownerResetPassword)).GetAwaiter().GetResult();
                this.pendingStatus = success ? "Owner reset complete" : "Owner reset failed";
            }
        }
    }
    private void DrawLegalModal()
    {
        if (!this.HasAcceptedLocalTerms())
        {
            ImGui.OpenPopup("TomestonePhone Legal Terms");
        }

        var modalSize = this.GetSetupModalSize();
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(modalSize, ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone Legal Terms", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped(LegalTerms.Summary);
            ImGui.Separator();
            var legalScrollHeight = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().Y - this.Scale(88f));
            using var child = ImRaii.Child("legal-scroll", new Vector2(0f, legalScrollHeight), true);
            if (child.Success)
            {
                ImGui.TextWrapped(LegalTerms.FullText);
            }

            ImGui.Checkbox("I have read and agree to the TomestonePhone terms on this computer.", ref this.localTermsCheckbox);

            if (ImGui.Button("Accept", new Vector2(100f, 28f)) && this.localTermsCheckbox)
            {
                this.configuration.AcceptedLegalTermsVersion = LegalTerms.Version;
                this.configuration.AcceptedLegalTermsAtUtc = DateTimeOffset.UtcNow;
                this.configuration.AcceptedLegalIdentity = this.configuration.Username ?? this.state.CurrentProfile.PhoneNumber;
                this.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            ImGui.BeginDisabled();
            ImGui.Button("Decline", new Vector2(100f, 28f));
            ImGui.EndDisabled();

            ImGui.EndPopup();
        }
    }

    private void DrawPrivacyModal()
    {
        if (this.HasAcceptedLocalTerms() && !this.HasAcceptedLocalPrivacy())
        {
            ImGui.OpenPopup("TomestonePhone Privacy Policy");
        }

        var modalSize = this.GetSetupModalSize();
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(modalSize, ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone Privacy Policy", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped(PrivacyPolicy.Summary);
            ImGui.Separator();
            var privacyScrollHeight = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().Y - this.Scale(88f));
            using var child = ImRaii.Child("privacy-scroll", new Vector2(0f, privacyScrollHeight), true);
            if (child.Success)
            {
                ImGui.TextWrapped(PrivacyPolicy.FullText);
            }

            ImGui.Checkbox("I have read and agree to the TomestonePhone privacy policy on this computer.", ref this.localPrivacyCheckbox);

            if (ImGui.Button("Accept", new Vector2(100f, 28f)) && this.localPrivacyCheckbox)
            {
                this.configuration.AcceptedPrivacyPolicyVersion = PrivacyPolicy.Version;
                this.configuration.AcceptedPrivacyPolicyAtUtc = DateTimeOffset.UtcNow;
                this.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
    private void DrawOpenEmoteSetupModal()
    {
        if (this.HasAcceptedLocalPrivacy() && !this.configuration.OpenEmoteSetupSeen)
        {
            ImGui.OpenPopup("TomestonePhone Opening Emote");
        }

        var modalSize = this.GetSetupModalSize(this.Scale(220f));
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(modalSize, ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone Opening Emote", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped("Would you like TomestonePhone to play /tomestonephone when you open the app with /ts or /tomestone?");
            ImGui.Spacing();
            ImGui.TextWrapped("This only plays on open, never on close, and you can change it later in Settings.");
            ImGui.Spacing();

            if (ImGui.Button("Keep Off", new Vector2(120f, 32f)))
            {
                this.configuration.PlayOpenEmote = false;
                this.configuration.OpenEmoteSetupSeen = true;
                this.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Turn On", new Vector2(120f, 32f)))
            {
                this.configuration.PlayOpenEmote = true;
                this.configuration.OpenEmoteSetupSeen = true;
                this.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private Vector2 GetSetupModalSize(float? minimumHeight = null)
    {
        var viewport = ImGui.GetMainViewport();
        var fallbackWindowSize = this.Size ?? new Vector2(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
        var phoneWindowSize = this.lastWindowSize.X > 0f && this.lastWindowSize.Y > 0f
            ? this.lastWindowSize
            : fallbackWindowSize;
        var minWidth = this.Scale(320f);
        var minHeight = minimumHeight ?? this.Scale(420f);
        var maxWidth = Math.Max(minWidth, viewport.WorkSize.X - this.Scale(32f));
        var maxHeight = Math.Max(minHeight, viewport.WorkSize.Y - this.Scale(32f));
        var width = Math.Clamp(phoneWindowSize.X - this.Scale(12f), minWidth, maxWidth);
        var height = Math.Clamp(phoneWindowSize.Y - this.Scale(12f), minHeight, maxHeight);
        return new Vector2(width, height);
    }

    private void DrawExternalLinkWarningModal()
    {
        if (this.showLinkWarningModal)
        {
            ImGui.OpenPopup("TomestonePhone External Link");
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(520f, 260f), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone External Link", ImGuiWindowFlags.NoResize))
        {
            this.showLinkWarningModal = false;
            ImGui.TextWrapped("You are about to open an external link in your web browser");
            ImGui.Separator();
            ImGui.TextWrapped("Only open links from people you trust");
            ImGui.TextWrapped("External sites may contain harmful, explicit, misleading, or unsafe content");
            ImGui.TextWrapped("Do not enter passwords, one-time codes, or personal information on a site you do not trust");
            ImGui.Separator();
            ImGui.TextWrapped(this.pendingExternalUrl);

            if (ImGui.Button("No", new Vector2(90f, 30f)))
            {
                this.pendingExternalUrl = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Yes", new Vector2(90f, 30f)))
            {
                this.OpenExternalUrl(this.pendingExternalUrl);
                this.pendingExternalUrl = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private bool HasAcceptedLocalTerms()
    {
        return this.configuration.AcceptedLegalTermsVersion == LegalTerms.Version;
    }

    private bool HasAcceptedLocalPrivacy()
    {
        return this.configuration.AcceptedPrivacyPolicyVersion == PrivacyPolicy.Version;
    }

    private string WrapBubbleText(string text, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0f)
        {
            return text;
        }

        var normalized = text.Replace("\r\n", "\n");
        var output = new System.Text.StringBuilder();
        var lines = normalized.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
            {
                output.Append('\n');
            }

            var line = lines[lineIndex];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var currentLine = new System.Text.StringBuilder();
            foreach (var word in line.Split(' '))
            {
                var currentText = currentLine.ToString();
                var candidate = currentLine.Length == 0 ? word : currentText + " " + word;
                if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                {
                    currentLine.Clear();
                    currentLine.Append(candidate);
                    continue;
                }

                if (currentLine.Length > 0)
                {
                    output.Append(currentLine);
                    output.Append('\n');
                    currentLine.Clear();
                }

                if (ImGui.CalcTextSize(word).X <= maxWidth)
                {
                    currentLine.Append(word);
                    continue;
                }

                var segment = new System.Text.StringBuilder();
                foreach (var ch in word)
                {
                    var next = segment.ToString() + ch;
                    if (segment.Length > 0 && ImGui.CalcTextSize(next).X > maxWidth)
                    {
                        output.Append(segment);
                        output.Append('\n');
                        segment.Clear();
                    }

                    segment.Append(ch);
                }

                currentLine.Append(segment);
            }

            output.Append(currentLine);
        }

        return output.ToString();
    }

    private void DrawMessageBubble(ChatMessageRecord message)
    {
        var isSender = string.Equals(message.SenderDisplayName, this.state.CurrentProfile.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.SenderDisplayName, this.state.CurrentProfile.Username, StringComparison.OrdinalIgnoreCase);
        var bubbleWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X * 0.76f);
        var bubblePadding = this.Scale(12f, 10f);
        var bubbleInnerWidth = Math.Max(this.Scale(96f), bubbleWidth - bubblePadding.X * 2f);
        var displayBody = message.IsDeletedForUsers ? "[Removed]" : message.Body ?? string.Empty;
        var wrappedBody = string.IsNullOrWhiteSpace(displayBody) ? string.Empty : this.WrapBubbleText(displayBody, bubbleInnerWidth);
        var textHeight = string.IsNullOrWhiteSpace(wrappedBody) ? 0f : ImGui.CalcTextSize(wrappedBody, false, bubbleInnerWidth).Y;
        var embedHeight = 0f;
        foreach (var embed in message.Embeds)
        {
            embedHeight += this.gifEmbedRenderer.IsGifUrl(embed.Url)
                ? this.Scale(188f)
                : ImGui.CalcTextSize(embed.Url, false, bubbleInnerWidth).Y + this.Scale(8f);
        }

        var bubbleHeight = Math.Max(this.Scale(36f), textHeight + embedHeight + bubblePadding.Y * 2f + (message.Embeds.Count > 0 && textHeight > 0f ? this.Scale(6f) : 0f));
        var cursorX = ImGui.GetCursorPosX();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (isSender)
        {
            ImGui.SetCursorPosX(cursorX + Math.Max(0f, availableWidth - bubbleWidth));
        }

        var bubbleMin = ImGui.GetCursorScreenPos();
        var bubbleMax = bubbleMin + new Vector2(bubbleWidth, bubbleHeight);
        var bubbleColor = isSender
            ? new Vector4(0.25f, 0.51f, 0.96f, 0.95f)
            : new Vector4(0.94f, 0.94f, 0.96f, 0.98f);
        var textColor = isSender ? Vector4.One : new Vector4(0.1f, 0.1f, 0.12f, 1f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(bubbleMin, bubbleMax, ImGui.GetColorU32(bubbleColor), this.Scale(18f));

        ImGui.SetCursorScreenPos(bubbleMin + bubblePadding);
        ImGui.PushTextWrapPos(bubbleMin.X + bubblePadding.X + bubbleInnerWidth);
        using (var textScope = ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            var wroteBody = false;
            if (!string.IsNullOrWhiteSpace(wrappedBody))
            {
                ImGui.TextUnformatted(wrappedBody);
                wroteBody = true;
            }

            foreach (var embed in message.Embeds)
            {
                if (wroteBody)
                {
                    ImGui.Spacing();
                    wroteBody = false;
                }

                if (this.gifEmbedRenderer.IsGifUrl(embed.Url))
                {
                    this.gifEmbedRenderer.Draw(embed.Url, bubbleInnerWidth, this.IsGifAnimationActive());
                    continue;
                }

                using var embedScope = ImRaii.PushColor(ImGuiCol.Text, isSender ? new Vector4(0.91f, 0.96f, 1f, 1f) : new Vector4(0.13f, 0.33f, 0.78f, 1f));
                if (ImGui.Selectable($"{embed.Url}##{embed.Id}", false, ImGuiSelectableFlags.None, new Vector2(bubbleInnerWidth, 0f)))
                {
                    this.pendingExternalUrl = embed.Url;
                    this.showLinkWarningModal = true;
                    ImGui.OpenPopup("TomestonePhone External Link");
                }
            }
        }

        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(new Vector2(bubbleMin.X, bubbleMax.Y + this.Scale(6f)));
        var meta = !isSender
            ? $"{message.SenderDisplayName}  {message.SentAtUtc.LocalDateTime:g}"
            : message.SentAtUtc.LocalDateTime.ToString("g");
        var metaWidth = ImGui.CalcTextSize(meta).X;
        if (isSender)
        {
            ImGui.SetCursorPosX(cursorX + Math.Max(0f, availableWidth - metaWidth));
        }

        ImGui.TextDisabled(meta);
    }

    private GameIdentityRecord? GetCurrentGameIdentity()
    {
#pragma warning disable CS0618
        var player = this.service.ObjectTable.LocalPlayer ?? this.service.ClientState.LocalPlayer;
#pragma warning restore CS0618
        if (player is null)
        {
            return null;
        }

        var characterName = player.Name.TextValue;
        var worldName = player.HomeWorld.Value.Name.ToString();
        return new GameIdentityRecord(characterName, worldName, $"{characterName}@{worldName}");
    }

    private bool IsGifAnimationActive()
    {
        return this.IsOpen && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
    }

    private void OpenExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            this.pendingStatus = "Invalid link";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });
            this.pendingStatus = "Link opened in browser";
        }
        catch
        {
            this.pendingStatus = "Could not open browser";
        }
    }


    private void BeginRegister()
    {
        if (this.configuration.LocalAccountLockout)
        {
            this.pendingStatus = "This computer is locked";
            return;
        }

        if (!this.HasAcceptedLocalTerms())
        {
            this.pendingStatus = "Accept the terms first";
            ImGui.OpenPopup("TomestonePhone Legal Terms");
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false })
        {
            return;
        }

        this.pendingStatus = "Creating account...";
        this.pendingAuthTask = this.RunRegisterAsync(this.loginUsername, this.loginPassword);
    }

    private void BeginLogin()
    {
        if (this.configuration.LocalAccountLockout)
        {
            this.pendingStatus = "This computer is locked";
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false })
        {
            return;
        }

        this.pendingStatus = "Signing in...";
        this.pendingAuthTask = this.RunLoginAsync(this.loginUsername, this.loginPassword);
    }

    private async Task<AuthResult> RunRegisterAsync(string username, string password)
    {
        try
        {
            var response = await this.client.RegisterAsync(username, password).ConfigureAwait(false);
            return new AuthResult(response.Username, response.AuthToken, "Account created", null);
        }
        catch (Exception ex)
        {
            return new AuthResult(null, null, null, ex);
        }
    }

    private async Task<AuthResult> RunLoginAsync(string username, string password)
    {
        try
        {
            var response = await this.client.LoginAsync(username, password).ConfigureAwait(false);
            return new AuthResult(response.Username, response.AuthToken, $"Signed in as {response.Username}", null);
        }
        catch (Exception ex)
        {
            return new AuthResult(null, null, null, ex);
        }
    }


    private void EnsureSessionHydrated()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            this.refreshOnNextDraw = false;
            return;
        }

        if (!this.refreshOnNextDraw)
        {
            return;
        }

        this.QueueSnapshotRefresh();
    }


    private IReadOnlyList<Guid> ResolveConversationTargets(string rawTargets)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return [];
        }

        var accountIds = new HashSet<Guid>();
        var targets = rawTargets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var target in targets)
        {
            var conversation = this.client.StartDirectConversationAsync(this.configuration.AuthToken, new StartDirectConversationRequest(target)).GetAwaiter().GetResult();
            var detail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
            var member = detail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId);
            if (member is not null)
            {
                accountIds.Add(member.AccountId);
            }
        }

        return accountIds.ToList();
    }

    private void TickMessageAutoRefresh()
    {
        if (!this.IsOpen || string.IsNullOrWhiteSpace(this.configuration.AuthToken) || this.showHomeScreen || this.activeTab != PhoneTab.Messages)
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false } || this.pendingConversationMessagesTask is { IsCompleted: false })
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (this.selectedConversationId is { } conversationId)
        {
            if (now - this.lastConversationRefreshUtc < TimeSpan.FromSeconds(2))
            {
                return;
            }

            this.lastConversationRefreshUtc = now;
            this.pendingConversationMessagesTask = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId);
            return;
        }

        if (now - this.lastConversationListRefreshUtc < TimeSpan.FromSeconds(4))
        {
            return;
        }

        this.lastConversationListRefreshUtc = now;
        this.RefreshSnapshot();
    }

    private void ProcessBackgroundTasks()
    {
        if (this.pendingVersionPolicyTask is { IsCompleted: true })
        {
            try
            {
                var policy = this.pendingVersionPolicyTask.GetAwaiter().GetResult();
                this.pendingVersionPolicyTask = null;
                this.clientVersionChecked = true;
                this.minimumClientVersion = policy.MinimumVersion ?? string.Empty;
                this.recommendedClientVersion = policy.RecommendedVersion ?? string.Empty;
                this.clientUpdateMessage = policy.UpdateMessage ?? string.Empty;
                this.clientRecommendedMessage = policy.RecommendedMessage ?? string.Empty;
                this.clientUpdateRequired = !string.IsNullOrWhiteSpace(this.minimumClientVersion)
                    && this.IsClientVersionOutdated(this.minimumClientVersion);
                if (this.clientUpdateRequired)
                {
                    this.clientRecommendedNoticeShown = false;
                    this.ApplyClientUpgradeRequired(this.minimumClientVersion, this.clientUpdateMessage);
                }
                else
                {
                    this.clientUpdateNoticeShown = false;
                    if (!string.IsNullOrWhiteSpace(this.recommendedClientVersion) && this.IsClientVersionOutdated(this.recommendedClientVersion))
                    {
                        this.AnnounceRecommendedVersionOnce();
                    }
                    else
                    {
                        this.clientRecommendedNoticeShown = false;
                    }
                    if (string.Equals(this.pendingStatus, "Update required", StringComparison.OrdinalIgnoreCase))
                    {
                        this.pendingStatus = "Connected";
                    }
                }
            }
            catch (Exception ex)
            {
                this.pendingVersionPolicyTask = null;
                this.clientVersionChecked = true;
                this.clientUpdateRequired = false;
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(ex.Message) ? this.pendingStatus : ex.Message);
            }
        }

        if (this.pendingAuthTask is { IsCompleted: true })
        {
            var result = this.pendingAuthTask.GetAwaiter().GetResult();
            this.pendingAuthTask = null;
            if (result.Error is not null)
            {
                this.HandleAuthFailure(result.Error);
            }
            else if (!string.IsNullOrWhiteSpace(result.Username) && !string.IsNullOrWhiteSpace(result.AuthToken))
            {
                this.configuration.Username = result.Username;
                this.configuration.AuthToken = result.AuthToken;
                this.configuration.StoreRememberedCredentials(this.loginUsername, this.loginPassword);
                this.pendingStatus = result.StatusMessage ?? "Signed in";
                this.ClearDebugAnnouncement();
                this.SaveConfiguration();
                this.showHomeScreen = true;
                this.autoLoginAttempted = false;
                this.refreshOnNextDraw = true;
                this.QueueSnapshotRefresh();
            }
        }

        if (this.pendingConversationMessagesTask is { IsCompleted: true })
        {
            try
            {
                var page = this.pendingConversationMessagesTask.GetAwaiter().GetResult();
                this.pendingConversationMessagesTask = null;
                if (this.selectedConversationId == page.ConversationId)
                {
                    var previousCount = this.selectedConversationMessages?.Messages.Count ?? 0;
                    this.selectedConversationMessages = page;
                    if (page.Messages.Count != previousCount)
                    {
                        this.scrollMessagesToBottom = true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.pendingConversationMessagesTask = null;
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(ex.Message) ? "Message refresh failed" : ex.Message);
                this.AnnounceDebugOnce($"Message refresh failed: {this.pendingStatus}", ex);
            }
        }

        if (this.pendingActiveCallsTask is { IsCompleted: true })
        {
            try
            {
                var sessions = this.pendingActiveCallsTask.GetAwaiter().GetResult();
                this.pendingActiveCallsTask = null;
                this.ProcessActiveCallSessions(sessions);
            }
            catch (Exception ex)
            {
                this.pendingActiveCallsTask = null;
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(ex.Message) ? "Call refresh failed" : ex.Message);
                this.AnnounceDebugOnce($"Call refresh failed: {this.pendingStatus}", ex);
            }
        }

        if (this.pendingSnapshotTask is { IsCompleted: true })
        {
            var result = this.pendingSnapshotTask.GetAwaiter().GetResult();
            this.pendingSnapshotTask = null;
            if (result.Error is not null)
            {
                if (result.Error is ClientUpgradeRequiredException upgradeRequired)
                {
                    this.ApplyClientUpgradeRequired(upgradeRequired.MinimumVersion, upgradeRequired.UpdateMessage);
                    return;
                }

                if (this.IsUnauthorizedError(result.Error))
                {
                    this.configuration.AuthToken = null;
                    this.SaveConfiguration();
                    if (this.TryBeginAutoLogin("Session expired. Restoring..."))
                    {
                        return;
                    }
                }

                this.refreshOnNextDraw = false;
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(result.Error.Message) ? "Sync failed" : result.Error.Message);
                this.AnnounceDebugOnce($"Sync failed: {this.pendingStatus}", result.Error);
                this.SignOutToGuestState(this.pendingStatus, false, false, false);
            }
            else if (result.Snapshot is not null)
            {
                this.state.ApplySnapshot(result.Snapshot);
                this.HandleServerAnnouncement(this.state.ActiveAnnouncement);
                if (result.UpdatedProfile is not null)
                {
                    this.state.CurrentProfile = result.UpdatedProfile;
                }

                if (this.state.CurrentProfile.Status == AccountStatus.Banned)
                {
                    this.configuration.LocalAccountLockout = true;
                    this.configuration.LocalAccountLockoutReason = "This device is locked because the linked account was banned.";
                    this.configuration.AuthToken = null;
                    this.configuration.Username = null;
                    this.configuration.ClearRememberedCredentials();
                    this.SaveConfiguration();
                    this.pendingStatus = "Device locked";
                }
                else
                {
                    this.refreshOnNextDraw = false;
                    this.ClearDebugAnnouncement();
                    this.pendingStatus = $"Synced {DateTime.Now:t}";
                }
            }

            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken) && this.snapshotRefreshQueued)
            {
                this.QueueSnapshotRefresh(this.snapshotRefreshQueuedSilently);
            }
        }
    }

    private bool HasHydratedAuthenticatedProfile()
    {
        return !string.IsNullOrWhiteSpace(this.configuration.AuthToken)
            && this.state.CurrentProfile.AccountId != Guid.Empty
            && !string.IsNullOrWhiteSpace(this.state.CurrentProfile.PhoneNumber)
            && !string.Equals(this.state.CurrentProfile.Username, "Guest", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryBeginAutoLogin(string statusMessage = "Restoring session...")
    {
        if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken) || this.configuration.LocalAccountLockout || this.autoLoginAttempted)
        {
            return false;
        }

        if (this.pendingAuthTask is { IsCompleted: false })
        {
            return true;
        }

        if (!this.configuration.TryGetRememberedCredentials(out var rememberedUsername, out var rememberedPassword))
        {
            return false;
        }

        this.loginUsername = rememberedUsername;
        this.loginPassword = rememberedPassword;
        this.pendingStatus = statusMessage;
        this.autoLoginAttempted = true;
        this.pendingAuthTask = this.RunLoginAsync(rememberedUsername, rememberedPassword);
        return true;
    }

    private bool IsUnauthorizedError(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("401")
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private void SignOutToGuestState(string statusMessage, bool clearRememberedCredentials = true, bool clearStoredUsername = true, bool resetAutoLoginAttempted = true)
    {
        var seeded = PhoneState.CreateSeeded();
        this.configuration.AuthToken = null;
        if (clearStoredUsername)
        {
            this.configuration.Username = null;
        }
        if (clearRememberedCredentials)
        {
            this.configuration.ClearRememberedCredentials();
        }
        this.state.CurrentProfile = seeded.CurrentProfile;
        this.state.Contacts = seeded.Contacts;
        this.state.BlockedContacts = seeded.BlockedContacts;
        this.state.Friends = seeded.Friends;
        this.state.Conversations = seeded.Conversations;
        this.state.RecentCalls = seeded.RecentCalls;
        this.state.FriendRequests = seeded.FriendRequests;
        this.state.Notifications = seeded.Notifications;
        this.state.VisibleReports = seeded.VisibleReports;
        this.state.VisibleAuditLogs = seeded.VisibleAuditLogs;
        this.state.SupportTickets = seeded.SupportTickets;
        this.state.ActiveCall = null;
        this.activeCallSessions = [];
        this.seenIncomingDirectCallSessionIds.Clear();
        this.selectedConversationId = null;
        this.selectedConversationMessages = null;
        this.selectedConversationDetail = null;
        this.showHomeScreen = false;
        this.activeTab = PhoneTab.Settings;
        this.pendingAuthTask = null;
        this.pendingSnapshotTask = null;
        this.refreshOnNextDraw = false;
        this.pendingStatus = statusMessage;
        this.autoLoginAttempted = resetAutoLoginAttempted ? false : this.autoLoginAttempted;
        if (!clearStoredUsername && !string.IsNullOrWhiteSpace(this.configuration.Username))
        {
            this.loginUsername = this.configuration.Username;
        }
        this.SaveConfiguration();
    }

    private async Task<PostAuthSnapshotResult> LoadPostAuthSnapshotAsync(string authToken, GameIdentityRecord? identity)
    {
        try
        {
            var snapshot = await this.client.GetSnapshotAsync(authToken).ConfigureAwait(false);
            PhoneProfile? profile = null;
            if (identity is not null)
            {
                try
                {
                    profile = await this.client.UpdateGameIdentityAsync(authToken, new UpdateGameIdentityRequest(identity.CharacterName, identity.WorldName)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.service.Log.Warning(ex, "TomestonePhone restored account data but failed to update the current game identity.");
                }
            }

            return new PostAuthSnapshotResult(snapshot, profile, null);
        }
        catch (Exception ex)
        {
            return new PostAuthSnapshotResult(null, null, ex);
        }
    }

    private sealed record AuthResult(string? Username, string? AuthToken, string? StatusMessage, Exception? Error);

    private sealed record PostAuthSnapshotResult(PhoneSnapshot? Snapshot, PhoneProfile? UpdatedProfile, Exception? Error);
}














































































































































