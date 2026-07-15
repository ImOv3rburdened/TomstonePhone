using System.Numerics;
using System.Text;
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
using TomestonePhone.Spelling;
using TomestonePhone.Shared.Models;
using TomestonePhone.Voice;

namespace TomestonePhone.UI;

public sealed class PhoneWindow : Window
{
    private sealed record WallpaperChoice(string Name, string Path, bool IsBundled);

    private enum SettingsPane
    {
        General,
        Icons,
    }

    private enum LocalImagePickerTarget
    {
        Wallpaper,
    }

    private sealed record CustomizableAppIcon(string Id, string Name, PhoneTab Tab, Func<string> GetPath, Action<string> SetPath, string DefaultPath);

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
    private const string KlipyCreateAppUrl = "https://partner.klipy.com/";
    private const double StartupSplashBlankSeconds = 1d;
    private const double StartupSplashLoadingSeconds = 2d;
    private const string StartupSplashBlankPath = "embedded://splash-screen-blank.png";
    private const string StartupSplashLoadingPath = "embedded://splash-screen-eorzea.png";
    private const string SpellFieldCompose = "compose-message";
    private const string SpellFieldFriendRequestMessage = "friend-request-message";
    private const string SpellFieldSupportSubject = "support-subject";
    private const string SpellFieldSupportBody = "support-body";
    private readonly Service service;
    private readonly Configuration configuration;
    private readonly PhoneState state;
    private readonly TomestonePhoneClient client;
    private readonly GifEmbedRenderer gifEmbedRenderer;
    private readonly AppIconRenderer appIconRenderer;
    private readonly VoiceChatSession voiceChatSession = new();
    private readonly GiphyClient giphyClient = new();
    private readonly SpellCheckService spellCheckService = new();
    private PhoneTab activeTab = PhoneTab.Messages;
    private bool showHomeScreen = true;
    private string loginUsername = string.Empty;
    private string loginPassword = string.Empty;
    private string pendingStatus = "Disconnected";
    private Vector2 lastWindowSize = new(DefaultWindowWidth * MinimumWindowScale, DefaultWindowHeight * MinimumWindowScale);
    private Vector2 lastPhoneWindowCenter;
    private bool localTermsCheckbox;
    private bool localPrivacyCheckbox;
    private string supportSubject = string.Empty;
    private string supportBody = string.Empty;
    private string oldPassword = string.Empty;
    private string newPassword = string.Empty;
    private string confirmPassword = string.Empty;
    private string deleteAccountPassword = string.Empty;
    private string deleteAccountError = string.Empty;
    private bool closeDeleteAccountPopup;
    private bool openDeleteAccountPasswordPopup;
    private string ownerResetTarget = string.Empty;
    private string ownerResetPassword = string.Empty;
    private AdminDashboardSnapshot? adminDashboard;
    private bool refreshStaffDashboardOnOpen = true;
    private string staffSearchQuery = string.Empty;
    private string staffTicketParticipantTarget = string.Empty;
    private Guid? selectedConversationId;
    private ConversationMessagePage? selectedConversationMessages;
    private ConversationDetail? selectedConversationDetail;
    private string composeMessage = string.Empty;
    private string composeEmbedUrl = string.Empty;
    private string gifSearchQuery = string.Empty;
    private IReadOnlyList<GiphyGifResult> gifSearchResults = [];
    private Task<IReadOnlyList<GiphyGifResult>>? pendingGifSearchTask;
    private bool openGifPicker;
    private string directMessageTarget = string.Empty;
    private ContactRecord? selectedDirectMessageContact;
    private string groupAddTarget = string.Empty;
    private bool showGroupMembersWindow;
    private Guid? pendingGroupRemoveMemberAccountId;
    private string pendingGroupRemoveMemberName = string.Empty;
    private Guid? pendingConversationDeleteId;
    private string pendingConversationDeleteName = string.Empty;
    private ChatModerationAction? pendingConversationDeleteAction;
    private string groupCreateName = string.Empty;
    private string groupCreateTargets = string.Empty;
    private readonly List<ContactRecord> groupCreateSelectedContacts = [];
    private string contactAddTarget = string.Empty;
    private IReadOnlyList<DirectoryPersonRecord> peopleSearchResults = [];
    private string callTarget = string.Empty;
    private ContactRecord? selectedCallContact;
    private string friendRequestTarget = string.Empty;
    private string friendRequestMessage = string.Empty;
    private string reportReplyBody = string.Empty;
    private string iconImportPath = string.Empty;
    private string localImagePickerDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private string localImagePickerSearch = string.Empty;
    private string localImagePickerFileName = string.Empty;
    private string? selectedLocalImagePath;
    private bool openLocalImagePicker;
    private LocalImagePickerTarget localImagePickerTarget = LocalImagePickerTarget.Wallpaper;
    private bool showIconSizeWarningModal;
    private string iconSizeWarningMessage = string.Empty;
    private SettingsPane activeSettingsPane = SettingsPane.General;
    private bool showLinkWarningModal;
    private string pendingExternalUrl = string.Empty;
    private int renderedMessageCount;
    private bool scrollMessagesToBottom = true;
    private bool clearComposeOnNextDraw;
    private bool focusComposeOnNextDraw;
    private int composeControlVersion;
    private int friendRequestMessageControlVersion;
    private int supportSubjectControlVersion;
    private int supportBodyControlVersion;
    private Task<ConversationMessagePage>? pendingConversationMessagesTask;
    private Task<ConversationDetail>? pendingConversationDetailTask;
    private DateTimeOffset lastConversationRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastConversationListRefreshUtc = DateTimeOffset.MinValue;
    private Task<AuthResult>? pendingAuthTask;
    private Task<PostAuthSnapshotResult>? pendingSnapshotTask;
    private readonly List<PendingUiOperation> pendingUiOperations = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private long uiOperationGeneration;
    private bool resourcesDisposed;
    private Task<IReadOnlyList<ActiveCallSessionRecord>>? pendingActiveCallsTask;
    private DateTimeOffset lastSnapshotRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastActiveCallRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastHeartbeatUtc = DateTimeOffset.MinValue;
    private List<ActiveCallSessionRecord> activeCallSessions = [];
    private HashSet<Guid> seenIncomingDirectCallSessionIds = [];
    private bool refreshOnNextDraw = true;
    private bool snapshotRefreshQueued;
    private bool snapshotRefreshQueuedSilently;
    private HashSet<Guid> knownIncomingFriendRequestIds = [];
    private int homePage;
    private float homePageDragOffset;
    private bool homePointerDown;
    private bool homePageDragging;
    private int? homePageSnapTarget;
    private Vector2 homePointerStart;
    private PhoneTab? homePressedApp;
    private PhoneTab? homeRenderedHoveredApp;
    private PhoneTab? homeDraggedApp;
    private bool homeLayoutChangedDuringDrag;
    private double homePressStartedAt;
    private float homeHoldElapsed;
    private bool homeEditMode;
    private double lastHomeDragPageChangeAt;
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
    private readonly GroupMembersOverlayWindow groupMembersOverlayWindow;
    private readonly NotificationOverlayWindow notificationOverlayWindow;
    private readonly CallOverlayWindow callOverlayWindow;
    private Guid? callOverlaySessionId;
    private IReadOnlyList<VoiceAudioDeviceInfo> voiceInputDevices = [];
    private IReadOnlyList<VoiceAudioDeviceInfo> voiceOutputDevices = [];
    private DateTimeOffset lastVoiceDeviceRefreshUtc = DateTimeOffset.MinValue;
    private readonly Dictionary<string, SpellFieldState> spellCheckFieldStates = [];
    private string? spellPopupFieldKey;
    private SpellCheckIssue? spellPopupIssue;
    private Vector2 spellPopupPosition;
    private float? pendingContactsScrollRestoreY;
    private float? pendingFriendsScrollRestoreY;
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
        this.groupMembersOverlayWindow = new GroupMembersOverlayWindow(this);
        this.notificationOverlayWindow = new NotificationOverlayWindow(this);
        this.callOverlayWindow = new CallOverlayWindow(this);
    }

    public void RegisterOverlayWindows(WindowSystem windows)
    {
        windows.AddWindow(this.groupMembersOverlayWindow);
        windows.AddWindow(this.notificationOverlayWindow);
        windows.AddWindow(this.callOverlayWindow);
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

    public override void OnClose()
    {
        this.groupMembersOverlayWindow.IsOpen = false;
        this.notificationOverlayWindow.IsOpen = false;
        this.callOverlayWindow.IsOpen = false;
    }

    public void DisposeResources()
    {
        if (this.resourcesDisposed)
        {
            return;
        }

        this.resourcesDisposed = true;
        this.InvalidateUiOperations();
        this.lifetimeCancellation.Cancel();
        this.voiceChatSession.Dispose();
        this.giphyClient.Dispose();
        this.gifEmbedRenderer.Dispose();
        this.appIconRenderer.Dispose();
        this.lifetimeCancellation.Dispose();
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

    private Vector2 MeasureTextAtFontSize(string text, float fontSize)
    {
        var measured = ImGui.CalcTextSize(text);
        var currentFontSize = Math.Max(1f, ImGui.GetFontSize());
        var fontScale = fontSize / currentFontSize;
        return new Vector2(measured.X * fontScale, measured.Y * fontScale);
    }

    private float FitTextToWidth(string text, float preferredFontSize, float maxWidth)
    {
        var safeMaxWidth = Math.Max(1f, maxWidth);
        var fontSize = Math.Max(1f, preferredFontSize);
        var measured = this.MeasureTextAtFontSize(text, fontSize);
        if (measured.X <= safeMaxWidth)
        {
            return fontSize;
        }

        return Math.Max(1f, fontSize * (safeMaxWidth / measured.X));
    }

    private void GetDockMetrics(float availableWidth, out float spacing, out float horizontalInset, out float cellWidth, out float iconSize, out float dockHeight)
    {
        spacing = this.Scale(16f);
        horizontalInset = this.Scale(18f);
        cellWidth = (availableWidth - horizontalInset * 2f - spacing * 2f) / 3f;
        iconSize = Math.Min(cellWidth * 0.82f, this.Scale(92f));
        dockHeight = iconSize * 1.4f;
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
        this.EnforceAspectRatio();

        var uiScale = this.GetUiScale();
        ImGui.SetWindowFontScale(uiScale);
        using var theme = PhoneTheme.Push(this.configuration, uiScale);
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

        this.DrawLegalModal();
        this.DrawPrivacyModal();
        this.DrawOpenEmoteSetupModal();
        this.DrawExternalLinkWarningModal();

        var rootBackground = this.PushTransparentScreenChildBackgroundIfNeeded();
        using var root = ImRaii.Child("TomestonePhoneRoot", new Vector2(-1f, -1f), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        rootBackground?.Dispose();
        if (!root.Success)
        {
            return;
        }

        this.SyncCallWindow();
        this.DrawHeader();
        ImGui.Separator();

        var footerHeight = this.Scale(28f);
        var contentSpacing = ImGui.GetStyle().ItemSpacing.Y;
        var contentHeight = Math.Max(this.Scale(120f), ImGui.GetContentRegionAvail().Y - footerHeight - contentSpacing);
        var contentBackground = this.PushTransparentScreenChildBackgroundIfNeeded();
        using (var content = ImRaii.Child("TomestonePhoneContent", new Vector2(-1f, contentHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            contentBackground?.Dispose();
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
                    case PhoneTab.Wallpapers:
                        this.DrawWallpapersApp();
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

        var footerBackground = this.PushTransparentScreenChildBackgroundIfNeeded();
        using (var footer = ImRaii.Child("TomestonePhoneFooter", new Vector2(-1f, footerHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            footerBackground?.Dispose();
            if (footer.Success)
            {
                this.DrawHomeButton();
            }
        }
    }

    public void TickBackground()
    {
        this.ProcessBackgroundTasks();
        this.EnsureClientVersionPolicy();
        if (this.clientVersionChecked && !this.clientUpdateRequired)
        {
            this.TryBeginAutoLogin();
            this.EnsureSessionHydrated();
            this.TickHeartbeat();
            this.TickMessageAutoRefresh();
            this.TickSnapshotAutoRefresh();
            this.TickActiveCallAutoRefresh();
        }

        this.SyncNotificationWindow();
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
        var statusLineHeight = ImGui.GetTextLineHeight();
        var statusOffsetY = topHeight + this.Scale(2f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(22f));
        draw.AddRect(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(22f));

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(6f)));
        ImGui.TextDisabled(DateTime.Now.ToString("h:mm"));
        var rightLabel = "Aether   |||   88%";
        var rightSize = ImGui.CalcTextSize(rightLabel);
        ImGui.SameLine(topWidth - rightSize.X - this.Scale(18f));
        ImGui.TextDisabled(rightLabel);

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(26f)));
        var title = string.IsNullOrWhiteSpace(this.configuration.AuthToken)
            ? "TomestonePhone"
            : this.showHomeScreen ? this.state.CurrentProfile.DisplayName : this.activeTab.ToString();
        ImGui.TextUnformatted(title);

        if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            var refreshWidth = this.Scale(78f);
            ImGui.SameLine(topWidth - refreshWidth - this.Scale(14f));
            if (this.DrawHeaderRefreshButton(new Vector2(refreshWidth, this.Scale(24f))))
            {
                this.refreshOnNextDraw = true;
                this.RefreshSnapshot();
            }
        }

        ImGui.SetCursorScreenPos(topStart + new Vector2(0f, statusOffsetY));
        ImGui.TextDisabled(this.pendingStatus);
        ImGui.SetCursorScreenPos(topStart);
        ImGui.Dummy(new Vector2(topWidth, statusOffsetY + statusLineHeight - this.Scale(2f)));
    }

    private bool DrawHeaderRefreshButton(Vector2 size)
    {
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, size.Y * 0.5f);
        using var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.055f));
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.105f));
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.15f));
        var clicked = ImGui.Button("##header-refresh", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.72f)), size.Y * 0.5f, ImDrawFlags.None, this.Scale(1f));

        const string label = "Refresh";
        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(min.X + (size.X - textSize.X) * 0.5f, min.Y + (size.Y - textSize.Y) * 0.5f);
        draw.AddText(textPosition + new Vector2(0f, this.Scale(1f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)), label);
        draw.AddText(textPosition, ImGui.GetColorU32(Vector4.One), label);
        return clicked;
    }

    private void DrawAuthStartScreen()
    {
        using var panel = ImRaii.Child("auth-start", new Vector2(-1f, -1f), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!panel.Success)
        {
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;
        using (var hero = ImRaii.Child("auth-hero", new Vector2(-1f, this.Scale(148f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
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
                using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
                {
                    ImGui.TextWrapped("Your account and phone number are restored automatically on this device once you sign in.");
                }
            }
        }

        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(4f)));
        ImGui.TextDisabled("Account");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##auth-username", "Username", ref this.loginUsername, 64);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##auth-password", "Password", ref this.loginPassword, 64, ImGuiInputTextFlags.Password);

        var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
        if (this.DrawPhonePillButton("Create Account", new Vector2(actionWidth, this.Scale(34f))))
        {
            this.BeginRegister();
        }
        ImGui.SameLine();
        if (this.DrawPhonePillButton("Sign In", new Vector2(actionWidth, this.Scale(34f))))
        {
            this.BeginLogin();
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(10f)));
        ImGui.TextDisabled("Before you continue");
        ImGui.TextWrapped("Terms and Privacy stay available inside the phone at any time.");
        var legalButtonWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
        if (this.DrawPhonePillButton("Terms", new Vector2(legalButtonWidth, this.Scale(34f))))
        {
            this.activeTab = PhoneTab.Legal;
        }
        ImGui.SameLine();
        if (this.DrawPhonePillButton("Privacy", new Vector2(legalButtonWidth, this.Scale(34f))))
        {
            this.activeTab = PhoneTab.Privacy;
        }
    }

    private void DrawHomeScreen()
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var totalHeight = ImGui.GetContentRegionAvail().Y;
        const int columns = 3;
        const int rows = 4;
        const int appsPerPage = columns * rows;
        var spacing = this.Scale(8f);
        var sideInset = this.Scale(6f);
        var topInset = this.Scale(6f);
        var bottomInset = this.Scale(2f);
        this.GetDockMetrics(totalWidth, out _, out _, out _, out _, out var dockHeight);
        var availableApps = new List<(string Label, string Glyph, PhoneTab Tab, int Badge)>
        {
            ("Friends", "F", PhoneTab.Friends, this.state.FriendRequests.Count(item => item.Status == FriendRequestStatus.Pending && item.IsIncoming)),
            ("Wallpapers", "W", PhoneTab.Wallpapers, 0),
            ("Settings", "S", PhoneTab.Settings, 0),
            ("Legal", "L", PhoneTab.Legal, 0),
            ("Privacy", "P", PhoneTab.Privacy, 0),
            ("Support", "?", PhoneTab.Support, 0)
        };

        if (this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator)
        {
            availableApps.Add(("Staff", "A", PhoneTab.Staff, this.state.VisibleReports.Count(item => item.Status == ReportStatus.Open)));
        }

        var appsByKey = availableApps.ToDictionary(item => item.Tab.ToString(), StringComparer.OrdinalIgnoreCase);
        var slots = this.configuration.HomeAppOrder
            .Select(value => value ?? string.Empty)
            .ToList();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < slots.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(slots[index]) || !appsByKey.ContainsKey(slots[index]) || !seenKeys.Add(slots[index]))
            {
                slots[index] = string.Empty;
            }
        }

        foreach (var app in availableApps.Where(item => !seenKeys.Contains(item.Tab.ToString())))
        {
            var emptySlot = slots.FindIndex(string.IsNullOrWhiteSpace);
            if (emptySlot >= 0)
            {
                slots[emptySlot] = app.Tab.ToString();
            }
            else
            {
                slots.Add(app.Tab.ToString());
            }
        }

        var lastPopulatedSlot = Math.Max(0, slots.FindLastIndex(value => !string.IsNullOrWhiteSpace(value)));
        var lastPopulatedPage = lastPopulatedSlot / appsPerPage;
        var maxPage = lastPopulatedPage + 1;
        while (slots.Count < (maxPage + 1) * appsPerPage)
        {
            slots.Add(string.Empty);
        }

        this.homePage = Math.Clamp(this.homePage, 0, maxPage);
        if (this.homePageSnapTarget is { } pendingPage)
        {
            pendingPage = Math.Clamp(pendingPage, 0, maxPage);
            this.homePageSnapTarget = pendingPage;
            var targetOffset = (this.homePage - pendingPage) * totalWidth;
            var smoothing = 1f - MathF.Exp(-10f * Math.Max(0.001f, ImGui.GetIO().DeltaTime));
            this.homePageDragOffset += (targetOffset - this.homePageDragOffset) * smoothing;
            if (Math.Abs(targetOffset - this.homePageDragOffset) <= this.Scale(0.75f))
            {
                this.homePage = pendingPage;
                this.homePageDragOffset = 0f;
                this.homePageSnapTarget = null;
            }
        }
        var usableHeight = Math.Max(this.Scale(180f), totalHeight - dockHeight - bottomInset - spacing);
        var cellWidth = (totalWidth - sideInset * 2f - spacing * (columns - 1)) / columns;
        var cellHeight = (usableHeight - topInset - spacing * (rows - 1)) / rows;
        var cell = MathF.Min(cellWidth, cellHeight);
        var gridWidth = cell * columns + spacing * (columns - 1);
        var pageOrigin = ImGui.GetCursorScreenPos();
        var gridInsetX = Math.Max(0f, (totalWidth - gridWidth) * 0.5f);
        var mouse = ImGui.GetIO().MousePos;
        var now = ImGui.GetTime();
        var leftHoldDuration = ImGui.GetIO().MouseDownDuration[0];
        var previousLeftHoldDuration = ImGui.GetIO().MouseDownDurationPrev[0];

        bool IsInside(Vector2 point, Vector2 min, Vector2 max) => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
        int SlotAt(Vector2 point)
        {
            for (var localIndex = 0; localIndex < appsPerPage; localIndex++)
            {
                var column = localIndex % columns;
                var row = localIndex / columns;
                var min = pageOrigin + new Vector2(gridInsetX + column * (cell + spacing), topInset + row * (cell + spacing));
                if (IsInside(point, min, min + new Vector2(cell, cell)))
                {
                    return this.homePage * appsPerPage + localIndex;
                }
            }

            return -1;
        }

        bool MoveAppToSlot(PhoneTab appTab, int targetSlot)
        {
            var sourceSlot = slots.FindIndex(value => string.Equals(value, appTab.ToString(), StringComparison.OrdinalIgnoreCase));
            if (sourceSlot < 0 || targetSlot < 0 || targetSlot >= slots.Count || sourceSlot == targetSlot)
            {
                return false;
            }

            var movingKey = slots[sourceSlot];
            slots[sourceSlot] = string.Empty;
            var carriedKey = movingKey;
            for (var slot = targetSlot; slot < slots.Count; slot++)
            {
                (slots[slot], carriedKey) = (carriedKey, slots[slot]);
                if (string.IsNullOrWhiteSpace(carriedKey))
                {
                    break;
                }
            }

            this.configuration.HomeAppOrder = slots.ToList();
            return true;
        }

        var pointerInsidePages = IsInside(mouse, pageOrigin, pageOrigin + new Vector2(totalWidth, usableHeight));
        if (this.homePageSnapTarget is null && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && pointerInsidePages)
        {
            this.homePointerDown = true;
            this.homePageDragging = false;
            this.homePointerStart = mouse;
            this.homePressedApp = this.homeRenderedHoveredApp;
            this.homePressStartedAt = now;
            this.homeHoldElapsed = 0f;
            if (this.homeEditMode)
            {
                this.homeDraggedApp = this.homePressedApp;
            }
        }

        if (this.homePointerDown && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = mouse - this.homePointerStart;
            var stationaryHold = pointerInsidePages
                && !this.homePageDragging
                && delta.Length() < this.Scale(30f);
            this.homeHoldElapsed = stationaryHold
                ? this.homeHoldElapsed + Math.Max(0f, ImGui.GetIO().DeltaTime)
                : 0f;
            if (!this.homeEditMode
                && !this.homePageDragging
                && (this.homeHoldElapsed >= 1.2f || leftHoldDuration >= 1.2f)
                && delta.Length() < this.Scale(30f))
            {
                this.homeEditMode = true;
                this.homeDraggedApp = this.homePressedApp;
                this.pendingStatus = "Editing Home Screen";
            }
            else if ((!this.homeEditMode || this.homeDraggedApp is null)
                && (this.homePageDragging || Math.Abs(delta.X) > this.Scale(18f))
                && Math.Abs(delta.X) > Math.Abs(delta.Y))
            {
                this.homePageDragging = true;
                var atBoundary = (this.homePage == 0 && delta.X > 0f) || (this.homePage == maxPage && delta.X < 0f);
                this.homePageDragOffset = atBoundary ? delta.X * 0.22f : delta.X;
            }

            if (this.homeEditMode && this.homeDraggedApp is not null && now - this.lastHomeDragPageChangeAt > 0.45d)
            {
                if (mouse.X < pageOrigin.X + this.Scale(24f) && this.homePage > 0)
                {
                    this.homePage--;
                    this.lastHomeDragPageChangeAt = now;
                }
                else if (mouse.X > pageOrigin.X + totalWidth - this.Scale(24f) && this.homePage < maxPage)
                {
                    this.homePage++;
                    this.lastHomeDragPageChangeAt = now;
                }
            }

            if (this.homeEditMode && this.homeDraggedApp is { } liveDraggedTab)
            {
                var hoverTarget = SlotAt(mouse);
                if (MoveAppToSlot(liveDraggedTab, hoverTarget))
                {
                    this.homeLayoutChangedDuringDrag = true;
                }
            }
        }

        if (this.homePointerDown && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            var completedLongPress = !this.homePageDragging
                && Math.Max(this.homeHoldElapsed, Math.Max(previousLeftHoldDuration, (float)(now - this.homePressStartedAt))) >= 1.2f
                && (mouse - this.homePointerStart).Length() < this.Scale(30f);
            if (this.homePageDragging)
            {
                var threshold = totalWidth * 0.30f;
                var targetPage = this.homePage;
                if (this.homePageDragOffset <= -threshold && this.homePage < maxPage)
                {
                    targetPage++;
                }
                else if (this.homePageDragOffset >= threshold && this.homePage > 0)
                {
                    targetPage--;
                }
                this.homePageSnapTarget = targetPage;
            }
            else if (completedLongPress)
            {
                this.homeEditMode = true;
            }
            else if (this.homeEditMode
                && this.homeDraggedApp is not null
                && (mouse - this.homePointerStart).Length() < this.Scale(10f))
            {
                this.homeEditMode = false;
            }
            else if (this.homeEditMode && this.homeDraggedApp is { } draggedTab)
            {
                var target = SlotAt(mouse);
                if (MoveAppToSlot(draggedTab, target))
                {
                    this.homeLayoutChangedDuringDrag = true;
                }
                if (this.homeLayoutChangedDuringDrag)
                {
                    this.SaveConfiguration();
                }
            }
            else if (this.homeEditMode && this.homePressedApp is null && (mouse - this.homePointerStart).Length() < this.Scale(10f))
            {
                this.homeEditMode = false;
            }
            else if (!completedLongPress && this.homePressedApp is { } openTab && (mouse - this.homePointerStart).Length() < this.Scale(10f))
            {
                this.showHomeScreen = false;
                this.activeTab = openTab;
            }

            this.homePointerDown = false;
            this.homePageDragging = false;
            if (this.homePageSnapTarget is null)
            {
                this.homePageDragOffset = 0f;
            }
            this.homePressedApp = null;
            this.homeDraggedApp = null;
            this.homeLayoutChangedDuringDrag = false;
            this.homeHoldElapsed = 0f;
        }

        using var transparentPages = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        using (var pageViewport = ImRaii.Child("home-pages", new Vector2(totalWidth, usableHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (pageViewport.Success)
            {
                var viewportOrigin = ImGui.GetWindowPos();
                this.homeRenderedHoveredApp = null;
                ImGui.SetCursorScreenPos(viewportOrigin);
                ImGui.InvisibleButton("##home-page-gesture-surface", new Vector2(totalWidth, usableHeight));
                if (this.homeEditMode)
                {
                    ImGui.SetCursorScreenPos(viewportOrigin + new Vector2(totalWidth - this.Scale(78f), this.Scale(4f)));
                    if (this.DrawPhonePillButton("Done##home-edit", new Vector2(this.Scale(70f), this.Scale(28f))))
                    {
                        this.homeEditMode = false;
                        this.homeDraggedApp = null;
                        this.homePointerDown = false;
                    }
                }
                for (var page = 0; page <= maxPage; page++)
                {
                    var pageX = (page - this.homePage) * totalWidth + this.homePageDragOffset;
                    for (var localIndex = 0; localIndex < appsPerPage; localIndex++)
                    {
                        var slot = page * appsPerPage + localIndex;
                        if (slot >= slots.Count || string.IsNullOrWhiteSpace(slots[slot]) || !appsByKey.TryGetValue(slots[slot], out var app))
                        {
                            continue;
                        }

                        if (this.homeEditMode && this.homeDraggedApp == app.Tab && this.homePointerDown)
                        {
                            continue;
                        }

                        var column = localIndex % columns;
                        var row = localIndex / columns;
                        var wiggleX = this.homeEditMode ? MathF.Sin((float)(now * 10d + slot * 1.7d)) * this.Scale(3f) : 0f;
                        var wiggleY = this.homeEditMode ? MathF.Cos((float)(now * 9d + slot * 1.3d)) * this.Scale(1.8f) : 0f;
                        var iconPosition = viewportOrigin + new Vector2(pageX + gridInsetX + column * (cell + spacing) + wiggleX, topInset + row * (cell + spacing) + wiggleY);
                        if (page == this.homePage && IsInside(mouse, iconPosition, iconPosition + new Vector2(cell, cell)))
                        {
                            this.homeRenderedHoveredApp = app.Tab;
                        }
                        ImGui.SetCursorScreenPos(iconPosition);
                        this.DrawAppIcon(app.Label, app.Glyph, app.Tab, app.Badge, cell, Vector4.Zero, Vector4.Zero, false);
                    }
                }

                if (this.homeEditMode && this.homeDraggedApp is { } floatingTab && appsByKey.TryGetValue(floatingTab.ToString(), out var floatingApp) && this.homePointerDown)
                {
                    ImGui.SetCursorScreenPos(mouse - new Vector2(cell * 0.5f));
                    this.DrawAppIcon(floatingApp.Label, floatingApp.Glyph, floatingApp.Tab, floatingApp.Badge, cell, Vector4.Zero, Vector4.Zero, false);
                }

            }
        }

        var dockCursorY = Math.Max(0f, totalHeight - dockHeight - bottomInset);
        ImGui.SetCursorPosY(dockCursorY);

        this.DrawDock();
    }

    private string GetAppIconPath(PhoneTab tab)
    {
        var path = tab switch
        {
            PhoneTab.Messages => this.configuration.MessagesIconPath,
            PhoneTab.Calls => this.configuration.CallsIconPath,
            PhoneTab.Contacts => this.configuration.ContactsIconPath,
            PhoneTab.Friends => this.configuration.FriendsIconPath,
            PhoneTab.Settings => this.configuration.SettingsIconPath,
            PhoneTab.Wallpapers => NormalizeLegacyWallpaperIconPath(this.configuration.WallpapersIconPath),
            PhoneTab.Legal => this.configuration.LegalIconPath,
            PhoneTab.Privacy => this.configuration.PrivacyIconPath,
            PhoneTab.Support => this.configuration.SupportIconPath,
            PhoneTab.Staff => this.configuration.StaffIconPath,
            _ => string.Empty,
        };
        return this.GetThemedBaseIconPath(path);
    }

    private static string NormalizeLegacyWallpaperIconPath(string path)
    {
        return path.Equals("embedded://icon.png", StringComparison.OrdinalIgnoreCase)
            ? "embedded://app-wallpapers.png"
            : path;
    }

    private string GetThemedBaseIconPath(string path)
    {
        if (!this.configuration.UseGreyscaleBaseIcons
            || !path.StartsWith("embedded://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path.ToLowerInvariant() switch
        {
            "embedded://app-phone.png" => "embedded://greyscale.app-phone.png",
            "embedded://app-contacts.png" => "embedded://greyscale.app-contacts.png",
            "embedded://app-friends.png" => "embedded://greyscale.app-friends.png",
            "embedded://app-legal.png" => "embedded://greyscale.app-legal.png",
            "embedded://app-messages.png" => "embedded://greyscale.app-messages.png",
            "embedded://app-privacy.png" => "embedded://greyscale.app-privacy.png",
            "embedded://app-settings.png" => "embedded://greyscale.app-settings.png",
            "embedded://app-staff.png" => "embedded://greyscale.app-staff.png",
            "embedded://app-support.png" => "embedded://greyscale.app-support.png",
            "embedded://app-wallpapers.png" => "embedded://greyscale.app-wallpapers.png",
            _ => path,
        };
    }

    private void DrawAppIcon(string label, string glyph, PhoneTab tab, int badgeCount, float width, Vector4 topColor, Vector4 bottomColor, bool openOnClick = true)
    {
        var cardHeight = width;
        using var transparentCell = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        using var group = ImRaii.Child($"app-{label}", new Vector2(width, cardHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!group.Success)
        {
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var labelFontSize = this.Scale(15f) * Math.Max(1f, width / 108f);
        labelFontSize = Math.Min(labelFontSize, this.Scale(25f));
        labelFontSize = this.FitTextToWidth(label, labelFontSize, width - this.Scale(18f));
        var labelSize = this.MeasureTextAtFontSize(label, labelFontSize);
        var iconTop = Math.Max(this.Scale(8f), width * 0.08f);
        var labelBottomPadding = Math.Max(this.Scale(10f), width * 0.08f);
        var iconSize = Math.Min(width * 0.66f, cardHeight * 0.68f);
        var iconMin = pos + new Vector2((width - iconSize) * 0.5f, iconTop);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconCorner = Math.Max(this.Scale(18f), iconSize * 0.18f);
        draw.AddRectFilled(
            iconMin + new Vector2(0f, Math.Max(this.Scale(5f), iconSize * 0.08f)),
            iconMax + new Vector2(0f, Math.Max(this.Scale(7f), iconSize * 0.1f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.14f)),
            iconCorner);

        var iconTexture = this.appIconRenderer.TryGetIcon(this.GetAppIconPath(tab));
        if (iconTexture is not null)
        {
            draw.AddImageRounded(iconTexture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), iconCorner);
            this.DrawIconTintOverlay(draw, iconMin, iconMax, iconCorner);
        }
        else
        {
            draw.AddRectFilledMultiColor(iconMin, iconMax, ImGui.GetColorU32(topColor), ImGui.GetColorU32(topColor), ImGui.GetColorU32(bottomColor), ImGui.GetColorU32(bottomColor));
            this.DrawIconTintOverlay(draw, iconMin, iconMax, iconCorner);
            draw.AddRect(iconMin, iconMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), iconCorner, ImDrawFlags.None, 1.2f);
            var glyphFontSize = Math.Clamp(iconSize * 0.48f, this.Scale(24f), this.Scale(46f));
            var glyphSize = this.MeasureTextAtFontSize(glyph, glyphFontSize);
            draw.AddText(ImGui.GetFont(), glyphFontSize, new Vector2(iconMin.X + (iconSize - glyphSize.X) * 0.5f, iconMin.Y + (iconSize - glyphSize.Y) * 0.5f - this.Scale(1f)), ImGui.GetColorU32(Vector4.One), glyph);
        }

        if (badgeCount > 0)
        {
            var badgeRadius = Math.Clamp(iconSize * 0.14f, this.Scale(13f), this.Scale(18f));
            var badgeCenter = new Vector2(iconMax.X - badgeRadius * 0.55f, iconMin.Y + badgeRadius * 0.55f);
            draw.AddCircleFilled(badgeCenter, badgeRadius, ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.25f, 1f)));
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeFontSize = Math.Clamp(badgeRadius * 0.95f, this.Scale(11f), this.Scale(15f));
            var badgeTextSize = this.MeasureTextAtFontSize(badgeText, badgeFontSize);
            draw.AddText(ImGui.GetFont(), badgeFontSize, new Vector2(badgeCenter.X - badgeTextSize.X * 0.5f, badgeCenter.Y - badgeTextSize.Y * 0.5f), ImGui.GetColorU32(Vector4.One), badgeText);
        }

        if (ImGui.InvisibleButton($"{label}##open", new Vector2(width, cardHeight)) && openOnClick)
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }

        this.DrawOutlinedText(draw, ImGui.GetFont(), labelFontSize, new Vector2(pos.X + (width - labelSize.X) * 0.5f, pos.Y + cardHeight - labelBottomPadding - labelSize.Y), label);
    }

    private void DrawDock()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var inset = this.Scale(8f);
        var start = ImGui.GetCursorScreenPos() + new Vector2(inset, 0f);
        width = Math.Max(this.Scale(220f), width - inset * 2f);
        this.GetDockMetrics(width, out var spacing, out var horizontalInset, out var cellWidth, out var iconSize, out var dockHeight);
        var draw = ImGui.GetWindowDrawList();
        var dockMax = start + new Vector2(width, dockHeight);
        var dockRounding = this.Scale(22f);
        draw.AddRectFilled(start + new Vector2(0f, this.Scale(3f)), dockMax + new Vector2(0f, this.Scale(3f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.2f)), dockRounding, ImDrawFlags.RoundCornersAll);
        draw.AddRectFilled(start, dockMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.13f)), dockRounding, ImDrawFlags.RoundCornersAll);
        draw.AddRect(start, dockMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.22f)), dockRounding, ImDrawFlags.RoundCornersAll, this.Scale(1f));

        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 0, "Calls", "C", PhoneTab.Calls, this.state.MissedCallCount, new Vector4(0.23f, 0.83f, 0.57f, 1f), new Vector4(0.12f, 0.56f, 0.37f, 1f));
        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 1, "Contacts", "P", PhoneTab.Contacts, 0, new Vector4(0.98f, 0.62f, 0.39f, 1f), new Vector4(0.86f, 0.43f, 0.22f, 1f));
        this.DrawDockIcon(start, horizontalInset, spacing, cellWidth, iconSize, 2, "Messages", "M", PhoneTab.Messages, this.state.UnreadConversationCount, new Vector4(0.28f, 0.6f, 0.98f, 1f), new Vector4(0.17f, 0.36f, 0.8f, 1f));
    }

    private void DrawDockIcon(Vector2 dockStart, float horizontalInset, float spacing, float cellWidth, float iconSize, int index, string label, string glyph, PhoneTab tab, int badgeCount, Vector4 topColor, Vector4 bottomColor)
    {
        var draw = ImGui.GetWindowDrawList();
        var x = dockStart.X + horizontalInset + index * (cellWidth + spacing);
        var y = dockStart.Y - iconSize * 0.4f;
        var iconMin = new Vector2(x + (cellWidth - iconSize) * 0.5f, y);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconCorner = Math.Max(this.Scale(18f), iconSize * 0.18f);
        draw.AddRectFilled(
            iconMin + new Vector2(0f, Math.Max(this.Scale(5f), iconSize * 0.08f)),
            iconMax + new Vector2(0f, Math.Max(this.Scale(8f), iconSize * 0.12f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.18f)),
            iconCorner);
        var iconTexture = this.appIconRenderer.TryGetIcon(this.GetAppIconPath(tab));
        if (iconTexture is not null)
        {
            draw.AddImageRounded(iconTexture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), iconCorner);
            this.DrawIconTintOverlay(draw, iconMin, iconMax, iconCorner);
        }
        else
        {
            draw.AddRectFilledMultiColor(iconMin, iconMax, ImGui.GetColorU32(topColor), ImGui.GetColorU32(topColor), ImGui.GetColorU32(bottomColor), ImGui.GetColorU32(bottomColor));
            this.DrawIconTintOverlay(draw, iconMin, iconMax, iconCorner);
            var glyphFontSize = Math.Clamp(iconSize * 0.48f, this.Scale(24f), this.Scale(42f));
            var glyphSize = this.MeasureTextAtFontSize(glyph, glyphFontSize);
            draw.AddText(ImGui.GetFont(), glyphFontSize, new Vector2(iconMin.X + (iconSize - glyphSize.X) * 0.5f, iconMin.Y + (iconSize - glyphSize.Y) * 0.5f), ImGui.GetColorU32(Vector4.One), glyph);
        }

        if (badgeCount > 0)
        {
            var badgeRadius = Math.Clamp(iconSize * 0.13f, this.Scale(11f), this.Scale(16f));
            var badgeCenter = new Vector2(iconMax.X - badgeRadius * 0.52f, iconMin.Y + badgeRadius * 0.52f);
            draw.AddCircleFilled(badgeCenter, badgeRadius, ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.25f, 1f)));
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeFontSize = Math.Clamp(badgeRadius * 0.95f, this.Scale(10f), this.Scale(14f));
            var badgeTextSize = this.MeasureTextAtFontSize(badgeText, badgeFontSize);
            draw.AddText(ImGui.GetFont(), badgeFontSize, new Vector2(badgeCenter.X - badgeTextSize.X * 0.5f, badgeCenter.Y - badgeTextSize.Y * 0.5f), ImGui.GetColorU32(Vector4.One), badgeText);
        }

        var labelFontSize = this.Scale(14f) * Math.Max(1f, cellWidth / 96f);
        labelFontSize = Math.Min(labelFontSize, this.Scale(21f));
        labelFontSize = this.FitTextToWidth(label, labelFontSize, cellWidth - this.Scale(10f));
        var labelSize = this.MeasureTextAtFontSize(label, labelFontSize);
        this.DrawOutlinedText(draw, ImGui.GetFont(), labelFontSize, new Vector2(x + (cellWidth - labelSize.X) * 0.5f, iconMax.Y + Math.Max(this.Scale(8f), iconSize * 0.12f)), label);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (ImGui.InvisibleButton($"{label}##dock", new Vector2(cellWidth, iconSize + this.Scale(42f))))
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }
    }

    private void DrawMessages()
    {
        if (this.selectedConversationId is { } selectedId && this.selectedConversationMessages is not null)
        {
            var selectedConversationIsReadOnly = this.selectedConversationDetail is { CanSendMessages: false };
            var detailHeight = this.GetSelectedConversationDetailHeight();
            var composerHeight = selectedConversationIsReadOnly
                ? this.Scale(64f)
                : Math.Max(
                    this.Scale(122f),
                    (ImGui.GetStyle().WindowPadding.Y * 2f) + this.Scale(58f) + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeightWithSpacing());
            var threadHeight = Math.Max(this.Scale(180f), ImGui.GetContentRegionAvail().Y - detailHeight - composerHeight - this.Scale(8f));

            if (this.selectedConversationDetail is not null)
            {
                using var details = ImRaii.Child("messages-detail-card", new Vector2(-1f, detailHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                if (details.Success)
                {
                    if (this.DrawPhonePillButton("Back To List", this.Scale(120f, 32f)))
                    {
                        this.selectedConversationId = null;
                        this.selectedConversationMessages = null;
                        this.selectedConversationDetail = null;
                        this.ResetConversationManagementState();
                        this.staffTicketParticipantTarget = string.Empty;
                        return;
                    }

                    var canOpenMembers = this.selectedConversationDetail.IsGroup && this.selectedConversationDetail.IsViewerActive;
                    if (canOpenMembers)
                    {
                        ImGui.SameLine();
                        if (this.DrawPhonePillButton("Members", new Vector2(this.Scale(118f), this.Scale(32f))))
                        {
                            this.OpenGroupMembersWindow(selectedId);
                        }
                    }

                    ImGui.Spacing();
                    ImGui.TextDisabled(this.selectedConversationDetail.Name);
                    var conversationStatusLine = this.GetSelectedConversationStatusLine();
                    if (!string.IsNullOrWhiteSpace(conversationStatusLine))
                    {
                        ImGui.TextDisabled(conversationStatusLine);
                    }

                    if (canOpenMembers)
                    {
                        this.UpdateGroupMembersWindowState(selectedId);
                    }
                    else
                    {
                        this.groupMembersOverlayWindow.IsOpen = false;
                    }

                    var linkedTicketId = this.selectedConversationDetail.LinkedSupportTicketId;
                    var isSupportConversation = linkedTicketId is not null;
                    var isStaff = this.IsCurrentUserStaff();
                    var activeSession = this.GetConversationActiveCallSession(selectedId);
                    var canInteractWithConversation = this.selectedConversationDetail.CanSendMessages;
                    if (activeSession is not null)
                    {
                        var durationLabel = (DateTimeOffset.UtcNow - activeSession.StartedUtc).ToString(@"hh\:mm\:ss");
                        ImGui.TextDisabled($"Active call - {durationLabel}");
                        var activeCallLabel = activeSession.IncludesCurrentAccount
                            ? (this.IsCurrentCallSession(activeSession.Id) ? (activeSession.IsGroup ? "Leave Call" : "End Call") : "Resume Call")
                            : "Join Call";
                        var canTouchActiveCall = activeSession.IncludesCurrentAccount && this.IsCurrentCallSession(activeSession.Id);
                        if (canTouchActiveCall || canInteractWithConversation)
                        {
                            if (this.DrawPhonePillButton(activeCallLabel, new Vector2(this.Scale(132f), this.Scale(32f))))
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
                    }
                    else if (this.selectedConversationDetail.IsGroup && canInteractWithConversation)
                    {
                        if (this.DrawPhonePillButton("Start Group Call", new Vector2(this.Scale(148f), this.Scale(32f))))
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
                        if (this.DrawPhonePillButton("Add Person", new Vector2(actionWidth, this.Scale(32f))))
                        {
                            this.AddSupportTicketParticipant(ticketId, this.staffTicketParticipantTarget, true);
                        }
                        ImGui.SameLine();
                        if (this.selectedConversationDetail.IsReadOnly)
                        {
                            using var closedDisabled = new ImRaii.DisabledDisposable().Push();
                            this.DrawPhonePillButton("Closed", new Vector2(actionWidth, this.Scale(32f)));
                        }
                        else if (this.DrawPhonePillButton("Close Ticket", new Vector2(actionWidth, this.Scale(32f))))
                        {
                            this.CloseSupportTicket(ticketId, true);
                        }
                    }
                    else if (!this.selectedConversationDetail.IsGroup && this.selectedConversationDetail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId) is { } otherMember && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(24f)) / 3f;
                        if (this.DrawPhonePillButton("Add Friend", new Vector2(actionWidth, this.Scale(32f))))
                        {
                            this.SendFriendRequest(otherMember.PhoneNumber);
                        }
                        ImGui.SameLine();
                        if (this.DrawPhonePillButton("Call", new Vector2(actionWidth, this.Scale(32f))))
                        {
                            this.BeginConversationCall(selectedId, false);
                        }
                        ImGui.SameLine();
                        if (this.DrawPhonePillButton("Block", new Vector2(actionWidth, this.Scale(32f))))
                        {
                            this.BlockAccount(otherMember.AccountId);
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
                    if (this.selectedConversationDetail is { CanSendMessages: false })
                    {
                        var composerMessage = this.GetSelectedConversationComposerMessage();
                        var disabledTextColor = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
                        using (ImRaii.PushColor(ImGuiCol.Text, disabledTextColor))
                        {
                            ImGui.TextWrapped(composerMessage);
                        }
                    }
                    else
                    {
                        if (this.clearComposeOnNextDraw)
                        {
                            this.composeMessage = string.Empty;
                            this.clearComposeOnNextDraw = false;
                        }

                        var helperText = "Enter sends. Shift+Enter adds a new line.";
                        var composeLineHeight = ImGui.GetTextLineHeightWithSpacing();
                        var framePadding = ImGui.GetStyle().FramePadding;
                        var itemSpacingY = ImGui.GetStyle().ItemSpacing.Y;
                        var availableHeight = ImGui.GetContentRegionAvail().Y;
                        var draftWrapWidth = Math.Max(this.Scale(120f), ImGui.GetContentRegionAvail().X - (framePadding.X * 2f) - this.Scale(18f));
                        var normalizedDraft = this.WrapDraftMessageText(this.composeMessage, draftWrapWidth);
                        if (!string.Equals(normalizedDraft, this.composeMessage, StringComparison.Ordinal))
                        {
                            this.composeMessage = normalizedDraft;
                        }

                        var draftLineCount = CountTextLines(this.composeMessage);
                        var minimumComposeInputHeight = this.Scale(58f);
                        var maximumComposeInputHeight = Math.Max(minimumComposeInputHeight, availableHeight - composeLineHeight - itemSpacingY);
                        var desiredComposeInputHeight = Math.Max(
                            minimumComposeInputHeight,
                            (framePadding.Y * 2f) + (composeLineHeight * draftLineCount) + this.Scale(6f));
                        var composeInputHeight = Math.Min(maximumComposeInputHeight, desiredComposeInputHeight);
                        var reservedHeight = composeInputHeight + itemSpacingY + composeLineHeight;
                        var topSpacer = Math.Max(0f, availableHeight - reservedHeight);
                        if (topSpacer > 0f)
                        {
                            ImGui.Dummy(new Vector2(0f, topSpacer));
                        }

                        if (this.focusComposeOnNextDraw)
                        {
                            ImGui.SetKeyboardFocusHere();
                            this.focusComposeOnNextDraw = false;
                        }
                        if (ImGui.InputTextMultiline(
                                $"##message-compose-{this.composeControlVersion}",
                                ref this.composeMessage,
                                1024,
                                new Vector2(-1f, composeInputHeight),
                                ImGuiInputTextFlags.NoHorizontalScroll))
                        {
                            var inputWrapWidth = Math.Max(this.Scale(120f), ImGui.GetItemRectSize().X - (framePadding.X * 2f) - this.Scale(18f));
                            var wrappedDraft = this.WrapDraftMessageText(this.composeMessage, inputWrapWidth);
                            if (!string.Equals(wrappedDraft, this.composeMessage, StringComparison.Ordinal))
                            {
                                this.composeMessage = wrappedDraft;
                            }
                        }
                        this.DrawSpellCheckOverlay(SpellFieldCompose, ref this.composeMessage, () => this.composeControlVersion++);
                        var sendPressed = ImGui.IsItemActive() &&
                            (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));
                        if (sendPressed && !ImGui.GetIO().KeyShift)
                        {
                            this.composeMessage = this.composeMessage.TrimEnd('\r', '\n');
                            this.SendComposedMessage(selectedId);
                        }
                        ImGui.TextDisabled(helperText);
                        ImGui.SameLine();
                        if (this.DrawPhonePillButton("GIF", new Vector2(this.Scale(58f), this.Scale(26f))))
                        {
                            this.openGifPicker = true;
                        }
                        this.DrawGifPicker(selectedId);
                    }
                }
            }

            return;
        }

        var selectedGroupMembersHeight = this.groupCreateSelectedContacts.Count == 0
            ? 0f
            : Math.Min(this.Scale(132f), this.Scale(34f) * this.groupCreateSelectedContacts.Count + this.Scale(28f));
        using (var compose = ImRaii.Child("messages-compose-card", new Vector2(-1f, this.Scale(196f) + selectedGroupMembersHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (compose.Success)
            {
                var ticketUnread = this.state.SupportTickets.Sum(ticket => this.state.Conversations.FirstOrDefault(item => item.Id == ticket.ConversationId)?.UnreadCount ?? 0);
                var staffUnread = this.state.Conversations.Where(this.IsStaffConversation).Sum(item => item.UnreadCount);
                var regularUnread = this.state.Conversations.Where(item => !this.IsTicketConversation(item) && !this.IsStaffConversation(item)).Sum(item => item.UnreadCount);
                var availableComposeWidth = ImGui.GetContentRegionAvail().X;
                var actionSpacing = this.Scale(8f);
                var tabWidth = this.IsCurrentUserStaff()
                    ? (availableComposeWidth - actionSpacing * 2f) / 3f
                    : (availableComposeWidth - actionSpacing) / 2f;
                if (this.DrawPhonePillButton(regularUnread > 0 ? $"Regular [{regularUnread}]" : "Regular", new Vector2(tabWidth, this.Scale(32f))))
                {
                    this.activeMessageFolder = MessageFolder.Regular;
                }
                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton(ticketUnread > 0 ? $"Tickets [{ticketUnread}]" : "Tickets", new Vector2(tabWidth, this.Scale(32f))))
                {
                    this.activeMessageFolder = MessageFolder.Tickets;
                }
                if (this.IsCurrentUserStaff())
                {
                    ImGui.SameLine(0f, actionSpacing);
                    if (this.DrawPhonePillButton(staffUnread > 0 ? $"Staff [{staffUnread}]" : "Staff", new Vector2(tabWidth, this.Scale(32f))))
                    {
                        this.activeMessageFolder = MessageFolder.Staff;
                    }
                }
                var buttonWidth = Math.Min(this.Scale(102f), Math.Max(this.Scale(88f), ImGui.CalcTextSize("New Chat").X + this.Scale(24f)));
                var inputWidth = Math.Max(this.Scale(120f), availableComposeWidth - buttonWidth - actionSpacing);
                ImGui.SetNextItemWidth(inputWidth);
                if (ImGui.InputTextWithHint("##direct-target", "Start typing a contact, username, or phone number", ref this.directMessageTarget, 64))
                {
                    this.selectedDirectMessageContact = null;
                }
                this.DrawContactSuggestionPopup(
                    "direct-target-picker",
                    this.GetMatchingContacts(this.directMessageTarget),
                    "Pick",
                    contact =>
                    {
                        this.selectedDirectMessageContact = contact;
                        this.directMessageTarget = contact.PhoneNumber;
                    });
                ImGui.SameLine(0f, actionSpacing);
                var directTarget = this.GetResolvedConversationTarget(this.selectedDirectMessageContact, this.directMessageTarget);
                if (this.DrawPhonePillButton("New Chat", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(directTarget))
                {
                    this.OpenDirectConversation(directTarget);
                    this.directMessageTarget = string.Empty;
                    this.selectedDirectMessageContact = null;
                }
                if (this.activeMessageFolder == MessageFolder.Regular)
                {
                    var groupButtonWidth = Math.Min(this.Scale(108f), Math.Max(this.Scale(94f), ImGui.CalcTextSize("New Group").X + this.Scale(24f)));
                    var groupNameWidth = Math.Max(this.Scale(120f), availableComposeWidth - groupButtonWidth - actionSpacing);
                    ImGui.SetNextItemWidth(groupNameWidth);
                    ImGui.InputTextWithHint("##group-name", "Group name", ref this.groupCreateName, 64);
                    ImGui.SameLine(0f, actionSpacing);
                    if (this.DrawPhonePillButton("New Group", new Vector2(groupButtonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.groupCreateName))
                    {
                        var authToken = this.configuration.AuthToken;
                        var groupName = this.groupCreateName.Trim();
                        var rawTargets = this.groupCreateTargets;
                        var selectedIds = this.groupCreateSelectedContacts.Select(contact => contact.Id).ToList();
                        this.QueueUiOperation("group-create", async () =>
                        {
                            var participantIds = new HashSet<Guid>(selectedIds);
                            foreach (var accountId in await this.ResolveConversationTargetsAsync(authToken, rawTargets).ConfigureAwait(false))
                            {
                                participantIds.Add(accountId);
                            }

                            if (participantIds.Count == 0)
                            {
                                throw new InvalidOperationException("Add at least one valid member");
                            }

                            return await this.client.CreateConversationAsync(authToken, new CreateConversationRequest(groupName, true, participantIds.ToList())).ConfigureAwait(false);
                        }, conversation =>
                        {
                                this.groupCreateName = string.Empty;
                                this.groupCreateTargets = string.Empty;
                                this.groupCreateSelectedContacts.Clear();
                                this.RefreshSnapshot();
                                this.OpenConversation(conversation.Id);
                                this.pendingStatus = "Group ready";
                        }, "Creating group...");
                    }
                    ImGui.InputTextWithHint("##group-members", "Start typing a contact, username, or phone number", ref this.groupCreateTargets, 256);
                    this.DrawContactSuggestionPopup(
                        "group-create-contact-picker",
                        this.GetMatchingContacts(this.groupCreateTargets, this.groupCreateSelectedContacts.Select(contact => contact.Id)),
                        "Add",
                        contact =>
                        {
                            if (this.groupCreateSelectedContacts.All(existing => existing.Id != contact.Id))
                            {
                                this.groupCreateSelectedContacts.Add(contact);
                            }

                            this.groupCreateTargets = string.Empty;
                        });
                    if (this.groupCreateSelectedContacts.Count > 0)
                    {
                        ImGui.TextDisabled("Selected Members");
                        foreach (var contact in this.groupCreateSelectedContacts.ToList())
                        {
                            ImGui.TextUnformatted(contact.DisplayName);
                            ImGui.TextDisabled(contact.PhoneNumber);
                            var removeWidth = Math.Max(this.Scale(96f), ImGui.CalcTextSize("Remove").X + this.Scale(28f));
                            var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - removeWidth);
                            ImGui.SetCursorPosX(maxX);
                            if (this.DrawPhonePillButton($"Remove##group-create-member-{contact.Id}", new Vector2(removeWidth, this.Scale(28f))))
                            {
                                this.groupCreateSelectedContacts.RemoveAll(existing => existing.Id == contact.Id);
                                break;
                            }

                            if (!ReferenceEquals(contact, this.groupCreateSelectedContacts.LastOrDefault()))
                            {
                                ImGui.Separator();
                            }
                        }
                    }
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
                var summaryStatus = conversation.IsViewerActive
                    ? (conversation.CanSendMessages ? string.Empty : "Closed")
                    : "Removed";
                var summaryMeta = string.IsNullOrWhiteSpace(summaryStatus)
                    ? $"{conversation.LastActivityUtc.LocalDateTime:t}  {(conversation.IsGroup ? "Group" : "Direct")}"
                    : $"{conversation.LastActivityUtc.LocalDateTime:t}  {(conversation.IsGroup ? "Group" : "Direct")}  {summaryStatus}";
                ImGui.TextDisabled(summaryMeta);

                var deleteAction = conversation.IsGroup
                    ? (conversation.IsOwner ? ChatModerationAction.DeleteConversation : ChatModerationAction.LeaveConversation)
                    : ChatModerationAction.HideConversation;
                var deleteLabel = deleteAction == ChatModerationAction.LeaveConversation ? "Leave" : "Delete";
                var openWidth = Math.Max(this.Scale(76f), ImGui.CalcTextSize("Open").X + this.Scale(28f));
                if (this.DrawPhonePillButton($"Open##{conversation.Id}", new Vector2(openWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.OpenConversation(conversation.Id, this.activeTab);
                }
                ImGui.SameLine();
                using (var deleteColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.48f, 0.17f, 0.2f, 0.9f)))
                using (var deleteHover = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.61f, 0.21f, 0.24f, 0.95f)))
                using (var deleteActive = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.38f, 0.12f, 0.14f, 1f)))
                {
                    var deleteWidth = Math.Max(this.Scale(86f), ImGui.CalcTextSize(deleteLabel).X + this.Scale(28f));
                    if (this.DrawPhonePillButton($"{deleteLabel}##{conversation.Id}", new Vector2(deleteWidth, this.Scale(32f))))
                    {
                        this.pendingConversationDeleteId = conversation.Id;
                        this.pendingConversationDeleteName = conversation.DisplayName;
                        this.pendingConversationDeleteAction = deleteAction;
                        ImGui.OpenPopup("Confirm?###confirm-conversation-delete");
                    }
                }
                ImGui.Separator();
            }

            var deleteConversationWarning = this.GetPendingConversationDeleteWarning();
            var deleteConversationConfirmLabel = this.GetPendingConversationDeleteConfirmLabel();
            this.PrepareConfirmModal(deleteConversationWarning, deleteConversationConfirmLabel, this.Scale(132f, 32f));
            using var deleteConversationWindowRounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f);
            using var deleteConversationPopupRounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 0f);
            using var deleteConversationTitlePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, this.Scale(10f, 3f));
            using var deleteConversationPopup = ImRaii.PopupModal("Confirm?###confirm-conversation-delete", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (deleteConversationPopup.Success)
            {
                this.DrawConfirmModalText(deleteConversationWarning);
                if (this.DrawPhonePillButton("Cancel", this.Scale(110f, 32f)))
                {
                    this.pendingConversationDeleteId = null;
                    this.pendingConversationDeleteName = string.Empty;
                    this.pendingConversationDeleteAction = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (this.DrawPhonePillButton(deleteConversationConfirmLabel, this.Scale(132f, 32f))
                    && this.pendingConversationDeleteId is Guid deleteConversationId
                    && this.pendingConversationDeleteAction is { } deleteConversationAction
                    && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.ModerateConversation(deleteConversationId, deleteConversationAction, null, deleteConversationAction switch
                        {
                            ChatModerationAction.DeleteConversation => "Conversation removed",
                            ChatModerationAction.LeaveConversation => "Left group",
                            _ => "Conversation removed from your list",
                        });

                    this.pendingConversationDeleteId = null;
                    this.pendingConversationDeleteName = string.Empty;
                    this.pendingConversationDeleteAction = null;
                    ImGui.CloseCurrentPopup();
                }
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
                if (ImGui.InputTextWithHint("##call-target", "Start typing a contact, username, or phone number", ref this.callTarget, 64))
                {
                    this.selectedCallContact = null;
                }
                this.DrawContactSuggestionPopup(
                    "call-target-picker",
                    this.GetMatchingContacts(this.callTarget),
                    "Pick",
                    contact =>
                    {
                        this.selectedCallContact = contact;
                        this.callTarget = contact.PhoneNumber;
                    });
                ImGui.SameLine();
                var callTarget = this.GetResolvedConversationTarget(this.selectedCallContact, this.callTarget);
                if (this.DrawPhonePillButton("Call", new Vector2(callButtonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(callTarget))
                {
                    this.BeginDirectCall(callTarget);
                    this.callTarget = string.Empty;
                    this.selectedCallContact = null;
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(10f)));

        using var history = ImRaii.Child("calls-history-card", new Vector2(-1f, 0f), true);
        if (!history.Success)
        {
            return;
        }

        var orderedActiveCalls = this.activeCallSessions
            .OrderByDescending(item => item.StartedUtc)
            .ToList();
        var orderedRecentCalls = this.state.RecentCalls
            .OrderByDescending(item => item.StartedUtc)
            .ToList();

        ImGui.TextDisabled("Active Calls");
        if (orderedActiveCalls.Count == 0)
        {
            ImGui.TextDisabled("No live calls right now");
        }
        else
        {
            for (var index = 0; index < orderedActiveCalls.Count; index++)
            {
                var session = orderedActiveCalls[index];
                ImGui.TextUnformatted(session.DisplayName);
                ImGui.TextDisabled($"{(session.IsGroup ? "Group" : "Direct")} - {session.StartedUtc.LocalDateTime:g} - {(DateTimeOffset.UtcNow - session.StartedUtc):hh\\:mm\\:ss}");
                var buttonLabel = session.IncludesCurrentAccount
                    ? (this.IsCurrentCallSession(session.Id) ? (session.IsGroup ? "Leave Call" : "End Call") : "Resume")
                    : "Join Call";
                var actionWidth = Math.Max(this.Scale(112f), ImGui.CalcTextSize(buttonLabel).X + this.Scale(28f));
                if (this.DrawPhonePillButton($"{buttonLabel}##active-call-{session.Id}", new Vector2(actionWidth, this.Scale(32f))))
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

                this.DrawStaffListSeparator(index, orderedActiveCalls.Count);
            }
        }

        if (orderedActiveCalls.Count > 0 && orderedRecentCalls.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
        }

        ImGui.TextDisabled("Recent Calls");
        if (orderedRecentCalls.Count == 0)
        {
            ImGui.TextDisabled("No calls yet");
            return;
        }

        for (var index = 0; index < orderedRecentCalls.Count; index++)
        {
            var call = orderedRecentCalls[index];
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

            var recallWidth = Math.Max(this.Scale(96f), ImGui.CalcTextSize("Call").X + this.Scale(28f));
            if (this.DrawPhonePillButton($"Call##recent-call-{call.Id}", new Vector2(recallWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                try
                {
                    this.BeginConversationCall(call.ConversationId, call.Kind == CallKind.Group);
                }
                catch (Exception ex)
                {
                    this.pendingStatus = this.SanitizeUserFacingError(ex.Message);
                }
            }

            this.DrawStaffListSeparator(index, orderedRecentCalls.Count);
        }
    }
    private void DrawContacts()
    {
        using (var add = ImRaii.Child("contacts-search-card", new Vector2(-1f, this.Scale(112f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (add.Success)
            {
                ImGui.TextDisabled("Search Contacts");
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputTextWithHint("##contact-target", "Username or phone number", ref this.contactAddTarget, 64))
                {
                    var query = this.contactAddTarget.Trim();
                    if (query.Length < 2 || string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        this.peopleSearchResults = [];
                    }
                    else
                    {
                        var authToken = this.configuration.AuthToken;
                        this.QueueUiOperation($"people-search-{query}", () => this.client.SearchPeopleAsync(authToken, query), results =>
                        {
                            if (string.Equals(this.contactAddTarget.Trim(), query, StringComparison.OrdinalIgnoreCase))
                            {
                                this.peopleSearchResults = results;
                            }
                        }, "Searching people...");
                    }
                }
                var actionSpacing = this.Scale(8f);
                var actionWidth = Math.Max(this.Scale(86f), (ImGui.GetContentRegionAvail().X - actionSpacing) * 0.5f);
                if (this.DrawPhonePillButton("Call##searched-contact", new Vector2(actionWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.contactAddTarget))
                {
                    this.BeginDirectCall(this.contactAddTarget);
                }

                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton("Message##searched-contact", new Vector2(actionWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.contactAddTarget))
                {
                    this.OpenDirectConversation(this.contactAddTarget);
                }
            }
        }

        using var contacts = ImRaii.Child("contacts-list-card", new Vector2(-1f, 0f), true);
        if (!contacts.Success)
        {
            return;
        }

        this.TryRestoreChildScroll(ref this.pendingContactsScrollRestoreY);

        ImGui.TextDisabled(string.IsNullOrWhiteSpace(this.contactAddTarget) ? "Contacts" : "Contacts and People");
        if (this.state.Contacts.Count == 0 && this.peopleSearchResults.Count == 0)
        {
            ImGui.TextDisabled("No contacts available");
            return;
        }

        var contactFilter = this.contactAddTarget.Trim();
        var savedContacts = this.state.Contacts
            .Where(item => contactFilter.Length == 0
                || item.DisplayName.Contains(contactFilter, StringComparison.OrdinalIgnoreCase)
                || item.PhoneNumber.Contains(contactFilter, StringComparison.OrdinalIgnoreCase));
        var sortedContacts = this.peopleSearchResults
            .Select(item => new ContactRecord(item.AccountId, item.DisplayName, item.PhoneNumber, string.Empty))
            .Concat(savedContacts)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => GetContactSortKey(item.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PhoneNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sortedContacts.Count == 0)
        {
            ImGui.TextDisabled("No matching contacts");
            return;
        }

        for (var index = 0; index < sortedContacts.Count; index++)
        {
            var contact = sortedContacts[index];
            this.DrawCopyableText(contact.DisplayName, contact.DisplayName, "Name copied");
            this.DrawCopyableText(contact.PhoneNumber, contact.PhoneNumber, "Phone number copied", true);
            if (!string.IsNullOrWhiteSpace(contact.Note))
            {
                this.DrawWrappedDisabledText(contact.Note);
            }

            var actionSpacing = this.Scale(8f);
            var buttonWidth = Math.Max(this.Scale(86f), (ImGui.GetContentRegionAvail().X - actionSpacing) * 0.5f);
            if (this.DrawPhonePillButton($"Call##{contact.Id}", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.BeginDirectCall(contact.PhoneNumber);
            }

            ImGui.SameLine(0f, actionSpacing);
            if (this.DrawPhonePillButton($"Message##{contact.Id}", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.OpenDirectConversation(contact.PhoneNumber);
            }

            if (index < sortedContacts.Count - 1)
            {
                ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
            }
        }
    }

    private static string GetContactSortKey(string displayName)
    {
        var label = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();
        if (label.Length == 0)
        {
            return string.Empty;
        }

        var worldSeparatorIndex = label.IndexOf(" @", StringComparison.Ordinal);
        var namePortion = worldSeparatorIndex >= 0 ? label[..worldSeparatorIndex].Trim() : label;
        if (namePortion.Length == 0)
        {
            namePortion = label;
        }

        var firstSpaceIndex = namePortion.IndexOf(' ');
        if (firstSpaceIndex < 0)
        {
            return $"{namePortion}|{label}";
        }

        var firstName = namePortion[..firstSpaceIndex].Trim();
        var remainingName = namePortion[(firstSpaceIndex + 1)..].Trim();
        return $"{firstName}|{remainingName}|{label}";
    }

    private string GetResolvedConversationTarget(ContactRecord? selectedContact, string rawTarget)
    {
        return selectedContact?.PhoneNumber ?? rawTarget.Trim();
    }

    private List<ContactRecord> GetMatchingContacts(string query, IEnumerable<Guid>? excludedIds = null, int maxResults = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var filter = query.Trim();
        var excluded = excludedIds?.ToHashSet() ?? [];
        return this.state.Contacts
            .Where(contact => contact.Id != this.state.CurrentProfile.AccountId)
            .Where(contact => !excluded.Contains(contact.Id))
            .Where(contact =>
                contact.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || contact.PhoneNumber.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(contact.Note) && contact.Note.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(contact => GetContactSortKey(contact.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(contact => contact.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contact => contact.PhoneNumber, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private void DrawContactSuggestionPopup(string popupId, IReadOnlyList<ContactRecord> contacts, string actionLabel, Action<ContactRecord> onSelect)
    {
        if (contacts.Count == 0)
        {
            return;
        }

        if (ImGui.IsItemActive() || ImGui.IsItemFocused())
        {
            ImGui.OpenPopup(popupId);
        }

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var popupHeight = Math.Min(this.Scale(220f), contacts.Count * this.Scale(52f) + this.Scale(12f));
        ImGui.SetNextWindowPos(new Vector2(itemMin.X, itemMax.Y + this.Scale(2f)));
        ImGui.SetNextWindowSize(new Vector2(itemMax.X - itemMin.X, popupHeight));

        using var popup = ImRaii.Popup(popupId);
        if (!popup.Success)
        {
            return;
        }

        using var pickerList = ImRaii.Child($"contact-picker-{popupId}", new Vector2(0f, 0f), true);
        if (pickerList.Success)
        {
            foreach (var contact in contacts)
            {
                ImGui.TextUnformatted(contact.DisplayName);
                ImGui.TextDisabled(contact.PhoneNumber);
                var actionWidth = Math.Max(this.Scale(84f), ImGui.CalcTextSize(actionLabel).X + this.Scale(28f));
                var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - actionWidth);
                ImGui.SetCursorPosX(maxX);
                if (this.DrawPhonePillButton($"{actionLabel}##{popupId}-{contact.Id}", new Vector2(actionWidth, this.Scale(28f))))
                {
                    onSelect(contact);
                    ImGui.CloseCurrentPopup();
                    break;
                }

                ImGui.Separator();
            }
        }
    }

    private void TryRestoreChildScroll(ref float? pendingScrollY)
    {
        if (!pendingScrollY.HasValue)
        {
            return;
        }

        ImGui.SetScrollY(Math.Max(0f, pendingScrollY.Value));
        if (this.pendingSnapshotTask is null)
        {
            pendingScrollY = null;
        }
    }

    private void DrawFriends()
    {
        using (var request = ImRaii.Child("friends-request-card", new Vector2(-1f, this.Scale(146f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (request.Success)
            {
                ImGui.TextDisabled("Send Friend Request");
                var buttonLabel = "Send Request";
                var buttonWidth = Math.Max(this.Scale(148f), ImGui.CalcTextSize(buttonLabel).X + this.Scale(34f));
                using var requestTable = ImRaii.Table("friend-request-compose", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
                if (requestTable.Success)
                {
                    ImGui.TableSetupColumn("Fields", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, buttonWidth);

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint("##friend-target", "Username or phone number", ref this.friendRequestTarget, 64);
                    ImGui.SetCursorPosY(Math.Max(0f, ImGui.GetCursorPosY() - this.Scale(6f)));
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint($"##friend-message-{this.friendRequestMessageControlVersion}", "Message", ref this.friendRequestMessage, 128);
                    this.DrawSpellCheckOverlay(SpellFieldFriendRequestMessage, ref this.friendRequestMessage, () => this.friendRequestMessageControlVersion++);

                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + this.Scale(4f));
                    if (this.DrawPhonePillButton(buttonLabel, new Vector2(buttonWidth, this.Scale(60f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.friendRequestTarget))
                    {
                        var authToken = this.configuration.AuthToken;
                        var target = this.friendRequestTarget;
                        var message = string.IsNullOrWhiteSpace(this.friendRequestMessage) ? null : this.friendRequestMessage;
                        this.QueueUiOperation("friend-request-create", () => this.client.CreateFriendRequestAsync(authToken, new FriendRequestCreateRequest(target, message)), created =>
                        {
                            this.friendRequestTarget = string.Empty;
                            this.friendRequestMessage = string.Empty;
                            this.friendRequestMessageControlVersion++;
                            this.pendingStatus = created.Status == FriendRequestStatus.Accepted ? "Friend paired" : "Friend request sent";
                            this.RefreshSnapshot();
                        }, "Sending friend request...");
                    }
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(10f)));

        using var list = ImRaii.Child("friends-list-card", new Vector2(-1f, 0f), true);
        if (!list.Success)
        {
            return;
        }

        this.TryRestoreChildScroll(ref this.pendingFriendsScrollRestoreY);

        var pendingRequests = this.state.FriendRequests
            .Where(item => item.Status == FriendRequestStatus.Pending)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.IsIncoming)
            .ToList();
        var sortedFriends = this.state.Friends
            .OrderBy(item => item.FriendDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FriendPhoneNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImGui.TextDisabled("Friends");
        if (pendingRequests.Count == 0 && sortedFriends.Count == 0)
        {
            ImGui.TextDisabled("No friends or requests yet");
            return;
        }

        if (pendingRequests.Count > 0)
        {
            ImGui.TextDisabled("Pending Requests");
            for (var index = 0; index < pendingRequests.Count; index++)
            {
                var request = pendingRequests[index];
                this.DrawCopyableText(request.DisplayName, request.DisplayName, "Name copied");
                this.DrawCopyableText(
                    request.IsIncoming ? $"Pending from {request.PhoneNumber}" : $"Pending to {request.PhoneNumber}",
                    request.PhoneNumber,
                    "Phone number copied",
                    true);

                if (request.IsIncoming)
                {
                    var actionSpacing = this.Scale(8f);
                    var buttonWidth = Math.Max(this.Scale(86f), (ImGui.GetContentRegionAvail().X - actionSpacing) * 0.5f);
                    if (this.DrawPhonePillButton($"Accept##{request.Id}", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var authToken = this.configuration.AuthToken;
                        this.pendingFriendsScrollRestoreY = ImGui.GetScrollY();
                        this.QueueUiOperation($"friend-accept-{request.Id}", () => this.client.RespondToFriendRequestAsync(authToken, new RespondFriendRequest(request.Id, true)), updated =>
                        {
                            this.pendingStatus = updated is not null ? "Friend paired" : "That friend request is no longer pending";
                            this.RefreshSnapshot();
                        }, "Accepting friend request...");
                    }

                    ImGui.SameLine(0f, actionSpacing);
                    if (this.DrawPhonePillButton($"Decline##{request.Id}", new Vector2(buttonWidth, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var authToken = this.configuration.AuthToken;
                        this.pendingFriendsScrollRestoreY = ImGui.GetScrollY();
                        this.QueueUiOperation($"friend-decline-{request.Id}", () => this.client.RespondToFriendRequestAsync(authToken, new RespondFriendRequest(request.Id, false)), updated =>
                        {
                            this.pendingStatus = updated is not null ? "Request declined" : "That friend request is no longer pending";
                            this.RefreshSnapshot();
                        }, "Declining friend request...");
                    }
                }
                else
                {
                    this.DrawWrappedDisabledText("Awaiting their response");
                }

                if (index < pendingRequests.Count - 1)
                {
                    ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                    ImGui.Separator();
                    ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                }
            }
        }

        if (pendingRequests.Count > 0 && sortedFriends.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
        }

        if (sortedFriends.Count > 0)
        {
            ImGui.TextDisabled("Current Friends");
            for (var index = 0; index < sortedFriends.Count; index++)
            {
                var friend = sortedFriends[index];
                this.DrawCopyableText(friend.FriendDisplayName, friend.FriendDisplayName, "Name copied");
                this.DrawCopyableText(friend.FriendPhoneNumber, friend.FriendPhoneNumber, "Phone number copied", true);
                this.DrawWrappedDisabledText($"Added {friend.SinceUtc.LocalDateTime:d}");
                if (this.DrawPhonePillButton($"Remove##{friend.FriendAccountId}", new Vector2(-1f, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var authToken = this.configuration.AuthToken;
                    this.pendingFriendsScrollRestoreY = ImGui.GetScrollY();
                    this.QueueUiOperation($"friend-remove-{friend.FriendAccountId}", () => this.client.RemoveFriendAsync(authToken, friend.FriendAccountId), removed =>
                    {
                        this.pendingStatus = removed ? "Friend unpaired" : "That friendship has already been removed";
                        this.RefreshSnapshot();
                    }, "Removing friend...");
                }

                if (index < sortedFriends.Count - 1)
                {
                    ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                    ImGui.Separator();
                    ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                }
            }
        }
    }
    private void DrawSessionRestoreScreen()
    {
        using var panel = ImRaii.Child("session-restore", new Vector2(-1f, -1f), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!panel.Success)
        {
            return;
        }

        using var card = ImRaii.Child("session-restore-card", new Vector2(-1f, this.Scale(220f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
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
        if (this.DrawPhonePillButton("Retry Now", this.Scale(128f, 34f)))
        {
            this.refreshOnNextDraw = true;
            this.RefreshSnapshot();
        }
        ImGui.SameLine();
        if (this.DrawPhonePillButton("Log Out", this.Scale(128f, 34f)))
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

        var tabWidth = (ImGui.GetContentRegionAvail().X - this.Scale(8f)) * 0.5f;
        if (this.DrawPhonePillButton("General", new Vector2(tabWidth, this.Scale(34f))))
        {
            this.activeSettingsPane = SettingsPane.General;
        }

        ImGui.SameLine();
        if (this.DrawPhonePillButton("Icons", new Vector2(tabWidth, this.Scale(34f))))
        {
            this.activeSettingsPane = SettingsPane.Icons;
        }

        ImGui.Separator();

        if (this.activeSettingsPane == SettingsPane.Icons)
        {
            this.DrawIconSettings();
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
            if (this.DrawPhonePillButton("Create Account", new Vector2(authButtonWidth, this.Scale(34f))))
            {
                this.BeginRegister();
            }
            ImGui.SameLine();
            if (this.DrawPhonePillButton("Sign In", new Vector2(authButtonWidth, this.Scale(34f))))
            {
                this.BeginLogin();
            }
            ImGui.Separator();
            ImGui.TextDisabled("Legal");
            if (this.DrawPhonePillButton("Terms", new Vector2(-1f, this.Scale(30f))))
            {
                this.activeTab = PhoneTab.Legal;
                return;
            }
            if (this.DrawPhonePillButton("Privacy", new Vector2(-1f, this.Scale(30f))))
            {
                this.activeTab = PhoneTab.Privacy;
                return;
            }
            return;
        }

        ImGui.TextDisabled("Your Number");
        var phoneNumber = this.GetPhoneNumberForUi();
        if (this.DrawPhonePillButton(phoneNumber, new Vector2(-1f, this.Scale(36f))))
        {
            ImGui.SetClipboardText(phoneNumber);
            this.pendingStatus = "Phone number copied";
        }
        ImGui.TextDisabled($"Username: {this.GetUsernameForUi()}");
        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        if (this.DrawPhonePillButton("Terms", new Vector2(-1f, this.Scale(30f))))
        {
            this.activeTab = PhoneTab.Legal;
            return;
        }
        if (this.DrawPhonePillButton("Privacy", new Vector2(-1f, this.Scale(30f))))
        {
            this.activeTab = PhoneTab.Privacy;
            return;
        }
        if (this.DrawPhonePillButton("Log Out", new Vector2(-1f, this.Scale(30f))))
        {
            this.SignOutToGuestState("Signed out");
            return;
        }
        if (this.state.CurrentProfile.Role == AccountRole.User && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            if (this.DrawPhonePillButton("Delete Account", new Vector2(-1f, this.Scale(30f))))
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

            this.PreparePhoneModal(this.Scale(320f, 215f));
            using var deleteAccountPopup = ImRaii.PopupModal("TomestonePhone Delete Account", ImGuiWindowFlags.NoResize);
            if (deleteAccountPopup.Success)
            {
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
            }

            this.PreparePhoneModal(this.Scale(320f, 195f));
            using var confirmDeleteAccountPopup = ImRaii.PopupModal("TomestonePhone Confirm Delete Account", ImGuiWindowFlags.NoResize);
            if (confirmDeleteAccountPopup.Success)
            {
                if (this.closeDeleteAccountPopup)
                {
                    this.closeDeleteAccountPopup = false;
                    ImGui.CloseCurrentPopup();
                    return;
                }

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
                    var authToken = this.configuration.AuthToken!;
                    var password = this.deleteAccountPassword;
                    this.QueueUiOperation("delete-account", () => this.client.DeleteAccountAsync(authToken, new DeleteAccountRequest(password)), success =>
                    {
                        if (success)
                        {
                            this.deleteAccountPassword = string.Empty;
                            this.deleteAccountError = string.Empty;
                            this.closeDeleteAccountPopup = true;
                            this.SignOutToGuestState("Account deleted");
                        }
                        else
                        {
                            this.deleteAccountError = "Invalid password";
                        }
                    }, "Deleting account...");
                }
            }
        }


        ImGui.Separator();
        ImGui.TextDisabled("Appearance");
        if (this.DrawEditableText("Accent Color", this.configuration.AccentColorHex, value => this.configuration.AccentColorHex = value, 16))
        {
            this.SaveConfiguration();
        }

        this.DrawIconTintSettings();
        if (this.DrawPhonePillButton("Customize App Icons", new Vector2(-1f, this.Scale(32f))))
        {
            this.activeSettingsPane = SettingsPane.Icons;
        }

        var lockViewport = this.configuration.LockViewport;
        if (ImGui.Checkbox("Lock viewport inside phone frame", ref lockViewport))
        {
            this.configuration.LockViewport = lockViewport;
            this.SaveConfiguration();
        }
        this.DrawNotificationAnchorPicker();

        ImGui.Separator();
        ImGui.TextDisabled("GIFs");
        ImGui.SetNextItemWidth(-1f);
        var klipyApiKey = this.configuration.KlipyApiKey;
        if (ImGui.InputTextWithHint("##klipy-api-key", "KLIPY API key", ref klipyApiKey, 256, ImGuiInputTextFlags.Password))
        {
            this.configuration.KlipyApiKey = klipyApiKey.Trim();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }
        if (this.DrawPhonePillButton("Get KLIPY API Key", new Vector2(-1f, this.Scale(30f))))
        {
            this.pendingExternalUrl = KlipyCreateAppUrl;
            this.showLinkWarningModal = true;
        }
        ImGui.TextDisabled("Used only for GIF search. Direct .gif links do not require a provider key.");

        ImGui.Separator();
        ImGui.TextDisabled("Writing");
        var enableSpellCheck = this.configuration.EnableSpellCheck;
        if (ImGui.Checkbox("Show spelling suggestions while typing", ref enableSpellCheck))
        {
            this.configuration.EnableSpellCheck = enableSpellCheck;
            this.SaveConfiguration();
            this.pendingStatus = enableSpellCheck
                ? "Spellcheck enabled"
                : "Spellcheck disabled";
        }

        if (this.configuration.EnableSpellCheck)
        {
            if (this.spellCheckService.IsAvailable)
            {
                ImGui.TextDisabled("English only for now. Click or right-click red-underlined words to review suggestions. Nothing changes automatically.");
            }
            else
            {
                using var spellcheckWarningColor = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.35f, 1f));
                ImGui.TextWrapped($"Spellcheck unavailable: {this.spellCheckService.AvailabilityMessage ?? "unknown error"}");
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Voice");
        this.RefreshVoiceDeviceCatalog();
        var inputResolution = VoiceAudioDeviceCatalog.ResolveInputDevice(this.voiceInputDevices, this.configuration.PreferredVoiceInputDeviceKey, this.configuration.PreferredVoiceInputDeviceName);
        var outputResolution = VoiceAudioDeviceCatalog.ResolveOutputDevice(this.voiceOutputDevices, this.configuration.PreferredVoiceOutputDeviceKey, this.configuration.PreferredVoiceOutputDeviceName);
        this.DrawVoiceDevicePicker(
            "Input Device",
            "##VoiceInputDevice",
            this.voiceInputDevices,
            inputResolution,
            this.configuration.PreferredVoiceInputDeviceKey,
            this.configuration.PreferredVoiceInputDeviceName,
            this.ApplyVoiceInputDevicePreference);
        this.DrawVoiceDevicePicker(
            "Output Device",
            "##VoiceOutputDevice",
            this.voiceOutputDevices,
            outputResolution,
            this.configuration.PreferredVoiceOutputDeviceKey,
            this.configuration.PreferredVoiceOutputDeviceName,
            this.ApplyVoiceOutputDevicePreference);

        var inputMissingMessage = GetSavedVoiceDeviceMissingMessage("input", inputResolution);
        if (!string.IsNullOrWhiteSpace(inputMissingMessage))
        {
            using var inputMissingColor = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.35f, 1f));
            ImGui.TextWrapped(inputMissingMessage);
        }

        var outputMissingMessage = GetSavedVoiceDeviceMissingMessage("output", outputResolution);
        if (!string.IsNullOrWhiteSpace(outputMissingMessage))
        {
            using var outputMissingColor = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.35f, 1f));
            ImGui.TextWrapped(outputMissingMessage);
        }

        var reduceVoiceBackgroundNoise = this.configuration.ReduceVoiceBackgroundNoise;
        if (ImGui.Checkbox("Reduce fan/background noise", ref reduceVoiceBackgroundNoise))
        {
            this.configuration.ReduceVoiceBackgroundNoise = reduceVoiceBackgroundNoise;
            this.SaveConfiguration();
            this.pendingStatus = reduceVoiceBackgroundNoise
                ? "Background noise reduction enabled"
                : "Background noise reduction disabled";
        }
        ImGui.TextDisabled("Uses a simple low-cut filter and speech gate. Best for fans and steady room noise.");

        var voiceMicVolumePercent = this.configuration.VoiceMicVolume * 100f;
        if (ImGui.SliderFloat("Mic Volume", ref voiceMicVolumePercent, 25f, 300f, "%.0f%%"))
        {
            this.configuration.VoiceMicVolume = voiceMicVolumePercent / 100f;
            this.voiceChatSession.SetLevels(this.configuration.VoiceMicVolume, this.configuration.VoiceOutputVolume);
            this.pendingStatus = $"Mic volume set to {voiceMicVolumePercent:0}%";
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }

        var voiceOutputVolumePercent = this.configuration.VoiceOutputVolume * 100f;
        if (ImGui.SliderFloat("Call Volume", ref voiceOutputVolumePercent, 25f, 300f, "%.0f%%"))
        {
            this.configuration.VoiceOutputVolume = voiceOutputVolumePercent / 100f;
            this.voiceChatSession.SetLevels(this.configuration.VoiceMicVolume, this.configuration.VoiceOutputVolume);
            this.pendingStatus = $"Call volume set to {voiceOutputVolumePercent:0}%";
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }
        ImGui.TextDisabled("These only affect TomestonePhone voice calls.");

        if (this.state.ActiveCall is not null)
        {
            ImGui.TextDisabled("Device changes apply on the next call. Volume changes apply immediately.");
        }

        ImGui.Separator();
        ImGui.TextDisabled("Account");
        var playOpenEmote = this.configuration.PlayOpenEmote;
        if (ImGui.Checkbox("Play /tomestone animation when opening via command", ref playOpenEmote))
        {
            this.configuration.PlayOpenEmote = playOpenEmote;
            this.SaveConfiguration();
        }
        ImGui.TextColored(new Vector4(0.96f, 0.74f, 0.33f, 1f), "Warning: This is an automation and is often frowned upon. Use at your own risk.");
        var shareGameIdentity = this.configuration.ShareGameIdentity;
        if (ImGui.Checkbox("Share current character/world as display name", ref shareGameIdentity))
        {
            this.configuration.ShareGameIdentity = shareGameIdentity;
            this.SaveConfiguration();
            this.SyncGameIdentityPreference();
        }
        ImGui.TextDisabled("Optional. This sends your character name and world to the configured backend for display names only.");
        var muted = this.state.CurrentProfile.NotificationsMuted;
        if (ImGui.Checkbox("Mute notifications", ref muted))
        {
            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var authToken = this.configuration.AuthToken;
                var requestedMuted = muted;
                this.QueueUiOperation("notification-settings", () => this.client.UpdateNotificationSettingsAsync(authToken, requestedMuted), profile =>
                {
                    this.state.CurrentProfile = profile;
                    if (requestedMuted)
                    {
                        this.state.Notifications.Clear();
                    }
                    this.pendingStatus = requestedMuted ? "Notifications muted" : "Notifications enabled";
                }, "Updating notification settings...");
            }
        }
        ImGui.TextDisabled("Current Password");
        ImGui.InputText("##CurrentPassword", ref this.oldPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.TextDisabled("New Password");
        ImGui.InputText("##NewPassword", ref this.newPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.TextDisabled("Confirm Password");
        ImGui.InputText("##ConfirmPassword", ref this.confirmPassword, 64, ImGuiInputTextFlags.Password);
        if (this.DrawPhonePillButton("Change Password", new Vector2(-1f, this.Scale(32f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            if (this.newPassword != this.confirmPassword)
            {
                this.pendingStatus = "New passwords do not match";
            }
            else
            {
                var authToken = this.configuration.AuthToken;
                var request = new PasswordResetSelfRequest(this.oldPassword, this.newPassword, this.confirmPassword);
                this.QueueUiOperation("change-password", () => this.client.ChangePasswordAsync(authToken, request), success =>
                {
                    this.pendingStatus = success ? "Password updated" : "Password update failed";
                    if (success)
                    {
                        this.oldPassword = string.Empty;
                        this.newPassword = string.Empty;
                        this.confirmPassword = string.Empty;
                    }
                }, "Changing password...");
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
                if (this.DrawPhonePillButton($"Unblock##{blockedContact.Id}", new Vector2(-1f, this.Scale(28f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var authToken = this.configuration.AuthToken;
                    this.QueueUiOperation($"unblock-{blockedContact.Id}", () => this.client.UnblockAccountAsync(authToken, blockedContact.Id), success =>
                    {
                        this.pendingStatus = success ? "Unblocked" : "Unblock failed";
                        if (success)
                        {
                            this.RefreshSnapshot();
                        }
                    }, "Unblocking contact...");
                }
            }
        }
    }

    private void DrawWallpapersApp()
    {
        using var wallpaperScroll = ImRaii.Child("wallpapers-app-scroll", new Vector2(-1f, 0f), true);
        if (!wallpaperScroll.Success)
        {
            return;
        }

        this.DrawOutlinedText(ImGui.GetWindowDrawList(), ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), "Wallpaper Gallery");
        ImGui.Dummy(ImGui.CalcTextSize("Wallpaper Gallery"));
        this.DrawOutlinedWrappedText("Pick a bundled wallpaper or import your own PNG/JPG. Imported wallpapers stay on this computer only.");

        var choices = this.GetWallpaperChoices();
        var galleryHeight = this.GetWallpaperCardHeight(includeDeleteButton: true) + this.Scale(22f);
        using (var gallery = ImRaii.Child("wallpaper-gallery", new Vector2(-1f, galleryHeight), true, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar))
        {
            if (gallery.Success)
            {
                for (var index = 0; index < choices.Count; index++)
                {
                    this.DrawWallpaperChoiceCard(choices[index]);
                    if (index < choices.Count - 1)
                    {
                        ImGui.SameLine(0f, this.Scale(12f));
                    }
                }
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Add Wallpaper");

        var importWidth = Math.Max(this.Scale(164f), ImGui.CalcTextSize("Import and Apply").X + this.Scale(30f));
        if (this.DrawPhonePillButton("Import and Apply", new Vector2(importWidth, this.Scale(32f))))
        {
            this.OpenLocalImagePicker(LocalImagePickerTarget.Wallpaper);
        }

        ImGui.SameLine();
        var resetWidth = Math.Max(this.Scale(96f), ImGui.CalcTextSize("Reset").X + this.Scale(30f));
        if (this.DrawPhonePillButton("Reset", new Vector2(resetWidth, this.Scale(32f))))
        {
            this.ResetBackgroundImage();
        }
        this.DrawLocalImagePickerPopup();

        ImGui.Separator();
        this.DrawWallpaperModeControls();
        ImGui.Separator();
        this.DrawSolidBackgroundSettings();
    }

    private void DrawOutlinedWrappedText(string text)
    {
        using var wrap = new ImRaii.TextWrapDisposable().Push(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        var size = ImGui.CalcTextSize(text, false, ImGui.GetContentRegionAvail().X);
        this.DrawOutlinedText(ImGui.GetWindowDrawList(), ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), text);
        ImGui.Dummy(size);
    }

    private void DrawSolidBackgroundSettings()
    {
        ImGui.TextDisabled("Solid Background");
        var useSolid = this.configuration.UseSolidBackgroundColor;
        if (ImGui.Checkbox("Use solid color behind wallpaper", ref useSolid))
        {
            this.configuration.UseSolidBackgroundColor = useSolid;
            this.SaveConfiguration();
        }

        if (this.DrawEditableText("Background Hex", this.configuration.SolidBackgroundColorHex, value => this.configuration.SolidBackgroundColorHex = value, 16))
        {
            this.SaveConfiguration();
        }

        var alphaPercent = this.configuration.SolidBackgroundAlpha * 100f;
        if (ImGui.SliderFloat("Background Alpha", ref alphaPercent, 0f, 100f, "%.0f%%"))
        {
            this.configuration.SolidBackgroundAlpha = Math.Clamp(alphaPercent / 100f, 0f, 1f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }
    }

    private void DrawWallpaperModeControls()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.BackgroundImagePath))
        {
            ImGui.TextDisabled("No wallpaper selected.");
            return;
        }

        ImGui.TextDisabled($"Active: {this.GetWallpaperDisplayName(this.configuration.BackgroundImagePath)}");
        var mode = this.configuration.BackgroundMode;
        using (var combo = ImRaii.Combo("Wallpaper Mode", GetWallpaperModeLabel(mode)))
        {
            if (combo.Success)
            {
                foreach (PhoneWallpaperMode value in Enum.GetValues(typeof(PhoneWallpaperMode)))
                {
                    var selected = value == mode;
                    if (ImGui.Selectable(GetWallpaperModeLabel(value), selected))
                    {
                        this.configuration.BackgroundMode = value;
                        if (value != PhoneWallpaperMode.Custom)
                        {
                            this.configuration.BackgroundZoom = 1f;
                            this.configuration.BackgroundOffsetX = 0f;
                            this.configuration.BackgroundOffsetY = 0f;
                        }

                        this.SaveConfiguration();
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
            }
        }

        if (this.configuration.BackgroundMode == PhoneWallpaperMode.Custom)
        {
            this.DrawWallpaperCustomEditor();
        }
    }

    private IReadOnlyList<WallpaperChoice> GetWallpaperChoices()
    {
        var choices = new List<WallpaperChoice>
        {
            new("Eorzea", StartupSplashLoadingPath, true),
            new("Midnight", StartupSplashBlankPath, true),
        };

        var directory = this.configuration.GetLocalWallpaperDirectory();
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(IsSupportedWallpaperFile)
                     .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase))
        {
            choices.Add(new(Path.GetFileNameWithoutExtension(path), path, false));
        }

        if (!string.IsNullOrWhiteSpace(this.configuration.BackgroundImagePath)
            && File.Exists(this.configuration.BackgroundImagePath)
            && choices.All(item => !string.Equals(item.Path, this.configuration.BackgroundImagePath, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new(this.GetWallpaperDisplayName(this.configuration.BackgroundImagePath), this.configuration.BackgroundImagePath, false));
        }

        return choices;
    }

    private static bool IsSupportedWallpaperFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private string GetWallpaperDisplayName(string path)
    {
        if (path.Equals(StartupSplashLoadingPath, StringComparison.OrdinalIgnoreCase))
        {
            return "Eorzea";
        }

        if (path.Equals(StartupSplashBlankPath, StringComparison.OrdinalIgnoreCase))
        {
            return "Midnight";
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private void DrawWallpaperChoiceCard(WallpaperChoice choice)
    {
        var cardWidth = this.Scale(120f);
        var previewHeight = this.Scale(154f);
        var cardHeight = this.GetWallpaperCardHeight(includeDeleteButton: !choice.IsBundled);
        using var card = ImRaii.Child($"wallpaper-card-{choice.Path}", new Vector2(cardWidth, cardHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!card.Success)
        {
            return;
        }

        var active = string.Equals(this.configuration.BackgroundImagePath, choice.Path, StringComparison.OrdinalIgnoreCase);
        var previewMin = ImGui.GetCursorScreenPos();
        var previewMax = previewMin + new Vector2(cardWidth, previewHeight);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(previewMin, previewMax, ImGui.GetColorU32(new Vector4(0.055f, 0.065f, 0.09f, 1f)), this.Scale(20f));
        var texture = this.appIconRenderer.TryGetTexture(choice.Path);
        if (texture is not null)
        {
            draw.AddImageRounded(texture.Handle, previewMin, previewMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), this.Scale(20f));
        }

        draw.AddRect(previewMin, previewMax, ImGui.GetColorU32(active ? new Vector4(0.52f, 0.72f, 1f, 0.9f) : new Vector4(1f, 1f, 1f, 0.14f)), this.Scale(20f), ImDrawFlags.None, active ? 2.2f : 1f);
        ImGui.InvisibleButton($"##wallpaper-preview-{choice.Path}", new Vector2(cardWidth, previewHeight));
        if (ImGui.IsItemClicked())
        {
            this.ApplyWallpaper(choice.Path);
        }

        var label = choice.IsBundled ? $"{choice.Name}*" : choice.Name;
        var visibleLabel = this.FitTextToWidth(label, this.Scale(14f), cardWidth - this.Scale(6f));
        var labelSize = this.MeasureTextAtFontSize(label, visibleLabel);
        draw.AddText(ImGui.GetFont(), visibleLabel, new Vector2(previewMin.X, previewMax.Y + this.Scale(8f)), ImGui.GetColorU32(Vector4.One), label);
        ImGui.SetCursorScreenPos(new Vector2(previewMin.X, previewMax.Y + this.Scale(8f) + labelSize.Y + this.Scale(8f)));
        if (this.DrawPhonePillButton(active ? "Active" : "Apply", new Vector2(-1f, this.Scale(30f))) && !active)
        {
            this.ApplyWallpaper(choice.Path);
        }

        if (!choice.IsBundled)
        {
            using var deleteColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.72f, 0.24f, 0.28f, 0.78f));
            using var deleteHoverColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.82f, 0.3f, 0.34f, 0.9f));
            using var deleteActiveColor = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.92f, 0.36f, 0.4f, 1f));
            if (this.DrawPhonePillButton("Delete", new Vector2(-1f, this.Scale(30f))))
            {
                this.DeleteUserWallpaper(choice.Path);
            }
        }
    }

    private float GetWallpaperCardHeight(bool includeDeleteButton)
    {
        var buttonCount = includeDeleteButton ? 2 : 1;
        return this.Scale(154f)
            + this.Scale(8f)
            + ImGui.GetTextLineHeight()
            + this.Scale(8f)
            + buttonCount * this.Scale(30f)
            + Math.Max(0, buttonCount - 1) * ImGui.GetStyle().ItemSpacing.Y
            + this.Scale(10f);
    }

    private void ApplyWallpaper(string path)
    {
        this.configuration.BackgroundImagePath = path;
        this.configuration.BackgroundMode = PhoneWallpaperMode.Fit;
        this.configuration.BackgroundZoom = 1f;
        this.configuration.BackgroundOffsetX = 0f;
        this.configuration.BackgroundOffsetY = 0f;
        this.SaveConfiguration();
        this.pendingStatus = $"Wallpaper applied: {this.GetWallpaperDisplayName(path)}";
    }

    private void DeleteUserWallpaper(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("embedded://", StringComparison.OrdinalIgnoreCase))
            {
                this.pendingStatus = "Bundled wallpapers cannot be deleted";
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var wallpaperDirectory = Path.GetFullPath(this.configuration.GetLocalWallpaperDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(wallpaperDirectory, StringComparison.OrdinalIgnoreCase))
            {
                this.pendingStatus = "Only imported wallpapers can be deleted";
                return;
            }

            var wasActive = string.Equals(this.configuration.BackgroundImagePath, path, StringComparison.OrdinalIgnoreCase);
            this.appIconRenderer.Invalidate(path);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            if (wasActive)
            {
                this.configuration.BackgroundImagePath = string.Empty;
                this.configuration.BackgroundMode = PhoneWallpaperMode.Fit;
                this.configuration.BackgroundZoom = 1f;
                this.configuration.BackgroundOffsetX = 0f;
                this.configuration.BackgroundOffsetY = 0f;
            }

            this.SaveConfiguration();
            this.pendingStatus = "Wallpaper deleted";
        }
        catch (Exception ex)
        {
            this.pendingStatus = $"Wallpaper delete failed: {this.SanitizeUserFacingError(ex.Message)}";
        }
    }

    private static string GetWallpaperModeLabel(PhoneWallpaperMode mode)
    {
        return mode switch
        {
            PhoneWallpaperMode.Stretch => "Stretch",
            PhoneWallpaperMode.Custom => "Custom drag/zoom",
            _ => "Scale to fit",
        };
    }

    private void DrawWallpaperCustomEditor()
    {
        this.DrawOutlinedText(ImGui.GetWindowDrawList(), ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), "Drag the preview to position it, then adjust zoom.");
        ImGui.Dummy(ImGui.CalcTextSize("Drag the preview to position it, then adjust zoom."));

        var available = ImGui.GetContentRegionAvail();
        var controlsWidth = Math.Min(this.Scale(220f), Math.Max(this.Scale(160f), available.X * 0.42f));
        var useSideControls = available.X >= this.Scale(360f);
        var reservedControlHeight = useSideControls ? this.Scale(10f) : this.Scale(128f);
        var previewHeight = Math.Clamp(available.Y - reservedControlHeight, this.Scale(120f), this.Scale(210f));
        var previewWidth = previewHeight * PhoneAspectRatio;
        if (useSideControls)
        {
            previewWidth = Math.Min(previewWidth, Math.Max(this.Scale(120f), available.X - controlsWidth - ImGui.GetStyle().ItemSpacing.X));
            previewHeight = previewWidth / PhoneAspectRatio;
        }
        else if (previewWidth > available.X)
        {
            previewWidth = available.X;
            previewHeight = previewWidth / PhoneAspectRatio;
        }

        var previewMin = ImGui.GetCursorScreenPos();
        var previewMax = previewMin + new Vector2(previewWidth, previewHeight);
        var previewRounding = this.Scale(22f);
        ImGui.InvisibleButton("##WallpaperPreviewDrag", new Vector2(previewWidth, previewHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var releasedDrag = ImGui.IsItemDeactivated();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(previewMin, previewMax, ImGui.GetColorU32(new Vector4(0.055f, 0.065f, 0.09f, 1f)), previewRounding);
        this.DrawWallpaper(previewMin, previewMax, previewRounding);
        draw.AddRect(previewMin, previewMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.22f : 0.1f)), previewRounding);

        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetIO().MouseDelta;
            this.configuration.BackgroundOffsetX = Math.Clamp(this.configuration.BackgroundOffsetX - (delta.X / Math.Max(1f, previewWidth)) * 2f, -1f, 1f);
            this.configuration.BackgroundOffsetY = Math.Clamp(this.configuration.BackgroundOffsetY - (delta.Y / Math.Max(1f, previewHeight)) * 2f, -1f, 1f);
        }

        if (releasedDrag)
        {
            this.SaveConfiguration();
        }

        if (useSideControls)
        {
            ImGui.SameLine();
            var controls = ImRaii.Child("wallpaper-custom-controls", new Vector2(-1f, previewHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!controls.Success)
            {
                controls.Dispose();
                return;
            }

            this.DrawWallpaperCustomEditorControls(useSideControls);
            controls.Dispose();
            return;
        }

        this.DrawWallpaperCustomEditorControls(useSideControls);
    }

    private void DrawWallpaperCustomEditorControls(bool compact)
    {
        var zoomPercent = Math.Clamp(this.configuration.BackgroundZoom, 1f, 2.75f) * 100f;
        if (ImGui.SliderFloat("Wallpaper Zoom", ref zoomPercent, 100f, 275f, "%.0f%%"))
        {
            this.configuration.BackgroundZoom = zoomPercent / 100f;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }

        var centerWidth = compact
            ? Math.Min(this.Scale(180f), ImGui.GetContentRegionAvail().X)
            : -1f;
        if (this.DrawPhonePillButton("Center Wallpaper", new Vector2(centerWidth, this.Scale(32f))))
        {
            this.configuration.BackgroundOffsetX = 0f;
            this.configuration.BackgroundOffsetY = 0f;
            this.configuration.BackgroundZoom = 1f;
            this.SaveConfiguration();
        }

        if (!compact || ImGui.GetContentRegionAvail().Y > this.Scale(44f))
        {
            this.DrawOutlinedText(ImGui.GetWindowDrawList(), ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), "Custom position and zoom are saved automatically.");
            ImGui.Dummy(ImGui.CalcTextSize("Custom position and zoom are saved automatically."));
        }
    }

    private void DrawIconSettings()
    {
        ImGui.TextDisabled("App Icons");
        ImGui.TextWrapped("Choose a PNG or JPG, then assign it to an app. Images must already be exactly 256x256 and keep the same rounded corners on the home screen.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##IconImportPath", "PNG or JPG file path", ref this.iconImportPath, 512);
        this.DrawIconTintSettings();
        this.DrawIconSizeWarningPopup();

        using var iconList = ImRaii.Child("settings-icon-list", new Vector2(-1f, 0f), true);
        if (!iconList.Success)
        {
            return;
        }

        var icons = this.GetCustomizableAppIcons();
        for (var index = 0; index < icons.Count; index++)
        {
            this.DrawIconSettingsRow(icons[index]);
            if (index < icons.Count - 1)
            {
                ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
            }
        }
    }

    private void DrawIconTintSettings()
    {
        ImGui.Separator();
        ImGui.TextDisabled("App Icon Theme");
        var useGreyscaleBaseIcons = this.configuration.UseGreyscaleBaseIcons;
        if (ImGui.Checkbox("Use greyscale bundled icons for tinting", ref useGreyscaleBaseIcons))
        {
            this.configuration.UseGreyscaleBaseIcons = useGreyscaleBaseIcons;
            this.SaveConfiguration();
        }

        var useIconTint = this.configuration.UseIconTint;
        if (ImGui.Checkbox("Apply color tint to app and dock icons", ref useIconTint))
        {
            this.configuration.UseIconTint = useIconTint;
            this.SaveConfiguration();
        }

        if (this.DrawEditableText("App/Dock Tint Hex", this.configuration.IconTintColorHex, value => this.configuration.IconTintColorHex = value, 16))
        {
            this.SaveConfiguration();
        }

        var tintRgb = this.ParseHexColor(this.configuration.IconTintColorHex, new Vector4(0.85f, 0.71f, 0.43f, 1f));
        var red = tintRgb.X * 255f;
        var green = tintRgb.Y * 255f;
        var blue = tintRgb.Z * 255f;
        var changedRgb = false;
        changedRgb |= ImGui.SliderFloat("Tint Red", ref red, 0f, 255f, "%.0f");
        var saveRgb = ImGui.IsItemDeactivatedAfterEdit();
        changedRgb |= ImGui.SliderFloat("Tint Green", ref green, 0f, 255f, "%.0f");
        saveRgb |= ImGui.IsItemDeactivatedAfterEdit();
        changedRgb |= ImGui.SliderFloat("Tint Blue", ref blue, 0f, 255f, "%.0f");
        saveRgb |= ImGui.IsItemDeactivatedAfterEdit();
        if (changedRgb)
        {
            this.configuration.IconTintColorHex = ToHexColor(red, green, blue);
        }

        if (saveRgb)
        {
            this.SaveConfiguration();
        }

        var tintAlpha = this.configuration.IconTintAlpha * 100f;
        if (ImGui.SliderFloat("App/Dock Tint Alpha", ref tintAlpha, 0f, 85f, "%.0f%%"))
        {
            this.configuration.IconTintAlpha = Math.Clamp(tintAlpha / 100f, 0f, 0.85f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            this.SaveConfiguration();
        }

        this.DrawIconTintPreview();
        if (this.DrawPhonePillButton("Reset App/Dock Tint", new Vector2(-1f, this.Scale(32f))))
        {
            this.configuration.UseIconTint = false;
            this.configuration.IconTintColorHex = "#D9B56D";
            this.configuration.IconTintAlpha = 0.22f;
            this.SaveConfiguration();
        }

        ImGui.TextDisabled("Tint draws over home and dock icons like a translucent iOS-style hue; it does not alter the saved images.");
        ImGui.Separator();
    }

    private void DrawIconTintPreview()
    {
        ImGui.TextDisabled("Live Preview");
        var previewHeight = this.Scale(108f);
        using var preview = ImRaii.Child("icon-tint-live-preview", new Vector2(-1f, previewHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!preview.Success)
        {
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var icons = new[]
        {
            (Label: "Calls", Path: this.GetAppIconPath(PhoneTab.Calls)),
            (Label: "Contacts", Path: this.GetAppIconPath(PhoneTab.Contacts)),
            (Label: "Messages", Path: this.GetAppIconPath(PhoneTab.Messages)),
        };
        var iconSize = this.Scale(58f);
        var cellWidth = this.Scale(92f);
        var totalWidth = cellWidth * icons.Length;
        var leftInset = Math.Max(0f, (availableWidth - totalWidth) * 0.5f);
        var topInset = Math.Max(0f, (previewHeight - iconSize - ImGui.GetTextLineHeight() - this.Scale(14f)) * 0.5f);

        for (var index = 0; index < icons.Length; index++)
        {
            var icon = icons[index];
            var cellX = start.X + leftInset + index * cellWidth;
            var iconMin = new Vector2(cellX + (cellWidth - iconSize) * 0.5f, start.Y + topInset);
            var iconMax = iconMin + new Vector2(iconSize, iconSize);
            var rounding = Math.Max(this.Scale(14f), iconSize * 0.18f);
            draw.AddRectFilled(iconMin + new Vector2(0f, this.Scale(5f)), iconMax + new Vector2(0f, this.Scale(7f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.18f)), rounding);
            var texture = this.appIconRenderer.TryGetIcon(icon.Path);
            if (texture is not null)
            {
                draw.AddImageRounded(texture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), rounding);
                this.DrawIconTintOverlay(draw, iconMin, iconMax, rounding);
            }

            var labelSize = ImGui.CalcTextSize(icon.Label);
            this.DrawOutlinedText(draw, ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(cellX + (cellWidth - labelSize.X) * 0.5f, iconMax.Y + this.Scale(8f)), icon.Label);
        }

        ImGui.Dummy(new Vector2(availableWidth, previewHeight - this.Scale(6f)));
    }

    private void DrawIconSizeWarningPopup()
    {
        if (this.showIconSizeWarningModal)
        {
            this.showIconSizeWarningModal = false;
            this.PreparePhoneModal(this.Scale(320f, 150f));
            ImGui.OpenPopup("Icon Resize Needed");
        }

        using var popup = ImRaii.PopupModal("Icon Resize Needed", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!popup.Success)
        {
            return;
        }

        ImGui.TextWrapped(this.iconSizeWarningMessage);
        ImGui.Spacing();
        if (this.DrawPhonePillButton("Okay", new Vector2(-1f, this.Scale(32f))))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private IReadOnlyList<CustomizableAppIcon> GetCustomizableAppIcons()
    {
        return
        [
            new("friends", "Friends", PhoneTab.Friends, () => this.configuration.FriendsIconPath, value => this.configuration.FriendsIconPath = value, "embedded://app-friends.png"),
            new("wallpapers", "Wallpapers", PhoneTab.Wallpapers, () => NormalizeLegacyWallpaperIconPath(this.configuration.WallpapersIconPath), value => this.configuration.WallpapersIconPath = value, "embedded://app-wallpapers.png"),
            new("settings", "Settings", PhoneTab.Settings, () => this.configuration.SettingsIconPath, value => this.configuration.SettingsIconPath = value, "embedded://app-settings.png"),
            new("legal", "Legal", PhoneTab.Legal, () => this.configuration.LegalIconPath, value => this.configuration.LegalIconPath = value, "embedded://app-legal.png"),
            new("privacy", "Privacy", PhoneTab.Privacy, () => this.configuration.PrivacyIconPath, value => this.configuration.PrivacyIconPath = value, "embedded://app-privacy.png"),
            new("support", "Support", PhoneTab.Support, () => this.configuration.SupportIconPath, value => this.configuration.SupportIconPath = value, "embedded://app-support.png"),
            new("staff", "Staff", PhoneTab.Staff, () => this.configuration.StaffIconPath, value => this.configuration.StaffIconPath = value, "embedded://app-staff.png"),
            new("calls", "Calls", PhoneTab.Calls, () => this.configuration.CallsIconPath, value => this.configuration.CallsIconPath = value, "embedded://app-phone.png"),
            new("contacts", "Contacts", PhoneTab.Contacts, () => this.configuration.ContactsIconPath, value => this.configuration.ContactsIconPath = value, "embedded://app-contacts.png"),
            new("messages", "Messages", PhoneTab.Messages, () => this.configuration.MessagesIconPath, value => this.configuration.MessagesIconPath = value, "embedded://app-messages.png"),
        ];
    }

    private void DrawIconSettingsRow(CustomizableAppIcon app)
    {
        var iconSize = this.Scale(56f);
        var rowStart = ImGui.GetCursorScreenPos();
        var configuredPath = app.GetPath();
        var displayPath = this.GetThemedBaseIconPath(configuredPath);
        var texture = this.appIconRenderer.TryGetIcon(displayPath);
        var draw = ImGui.GetWindowDrawList();
        var iconMax = rowStart + new Vector2(iconSize, iconSize);
        var iconCorner = Math.Max(this.Scale(14f), iconSize * 0.18f);
        draw.AddRectFilled(rowStart + new Vector2(0f, this.Scale(4f)), iconMax + new Vector2(0f, this.Scale(7f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.16f)), iconCorner);
        if (texture is not null)
        {
            draw.AddImageRounded(texture.Handle, rowStart, iconMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), iconCorner);
        }
        else
        {
            draw.AddRectFilled(rowStart, iconMax, ImGui.GetColorU32(new Vector4(0.22f, 0.27f, 0.38f, 1f)), iconCorner);
        }

        ImGui.Dummy(new Vector2(iconSize, iconSize));
        ImGui.SameLine();
        using var details = ImRaii.Child($"icon-settings-row-{app.Id}", new Vector2(-1f, this.Scale(82f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!details.Success)
        {
            return;
        }

        ImGui.TextUnformatted(app.Name);
        ImGui.TextDisabled(configuredPath.StartsWith("embedded://", StringComparison.OrdinalIgnoreCase) ? "Using bundled icon" : Path.GetFileName(configuredPath));
        var buttonWidth = (ImGui.GetContentRegionAvail().X - this.Scale(8f)) * 0.5f;
        if (this.DrawPhonePillButton($"Apply Image##icon-apply-{app.Id}", new Vector2(buttonWidth, this.Scale(30f))))
        {
            this.TryImportAppIcon(app, this.iconImportPath);
        }

        ImGui.SameLine();
        if (this.DrawPhonePillButton($"Reset##icon-reset-{app.Id}", new Vector2(buttonWidth, this.Scale(30f))))
        {
            this.ResetAppIcon(app);
        }
    }

    private void TryImportAppIcon(CustomizableAppIcon app, string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                this.pendingStatus = "Choose a PNG or JPG icon first";
                return;
            }

            var trimmed = sourcePath.Trim().Trim('"');
            if (!File.Exists(trimmed) || !IsSupportedWallpaperFile(trimmed))
            {
                this.pendingStatus = "Icon must be a PNG or JPG file";
                return;
            }

            var destinationPath = this.configuration.GetLocalIconImportPath(app.Id, trimmed);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var image = Image.Load<Rgba32>(trimmed);
            if (image.Width != 256 || image.Height != 256)
            {
                this.iconSizeWarningMessage = $"That icon is {image.Width}x{image.Height}. Please resize it to exactly 256x256 and try again.";
                this.showIconSizeWarningModal = true;
                this.pendingStatus = "Icon must be exactly 256x256";
                return;
            }

            image.Save(destinationPath, new PngEncoder());

            this.appIconRenderer.Invalidate(app.GetPath());
            app.SetPath(destinationPath);
            this.iconImportPath = string.Empty;
            this.SaveConfiguration();
            this.pendingStatus = $"{app.Name} icon updated";
        }
        catch (Exception ex)
        {
            this.pendingStatus = $"Icon import failed: {this.SanitizeUserFacingError(ex.Message)}";
        }
    }

    private void ResetAppIcon(CustomizableAppIcon app)
    {
        this.appIconRenderer.Invalidate(app.GetPath());
        app.SetPath(app.DefaultPath);
        this.SaveConfiguration();
        this.pendingStatus = $"{app.Name} icon reset";
    }

    private void OpenLocalImagePicker(LocalImagePickerTarget target)
    {
        this.localImagePickerTarget = target;
        if (string.IsNullOrWhiteSpace(this.localImagePickerDirectory) || !Directory.Exists(this.localImagePickerDirectory))
        {
            this.localImagePickerDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(this.localImagePickerDirectory) || !Directory.Exists(this.localImagePickerDirectory))
            {
                this.localImagePickerDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }

        this.openLocalImagePicker = true;
    }

    private void DrawLocalImagePickerPopup()
    {
        if (this.openLocalImagePicker)
        {
            this.openLocalImagePicker = false;
            this.selectedLocalImagePath = null;
            this.localImagePickerFileName = string.Empty;
            this.PreparePhoneModal(this.Scale(760f, 520f));
            ImGui.OpenPopup("Pick Image");
        }

        using var pickerWindowRounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f);
        using var pickerPopupRounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 0f);
        using var pickerChildRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0f);
        using var pickerFrameRounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f);
        using var pickerGrabRounding = ImRaii.PushStyle(ImGuiStyleVar.GrabRounding, 0f);
        using var popup = ImRaii.PopupModal("Pick Image", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!popup.Success)
        {
            return;
        }

        this.DrawLocalImagePickerToolbar();
        ImGui.Separator();

        var sidebarWidth = this.Scale(108f);
        var bodyHeight = this.Scale(288f);
        using (var sidebar = ImRaii.Child("local-image-picker-sidebar", new Vector2(sidebarWidth, bodyHeight), true))
        {
            if (sidebar.Success)
            {
                this.DrawLocalImagePickerSidebar();
            }
        }

        ImGui.SameLine();
        var previewWidth = this.Scale(156f);
        var fileWidth = Math.Max(this.Scale(390f), ImGui.GetContentRegionAvail().X - previewWidth - ImGui.GetStyle().ItemSpacing.X);
        using (var files = ImRaii.Child("local-image-picker-files", new Vector2(fileWidth, bodyHeight), true))
        {
            if (files.Success)
            {
                this.DrawLocalImagePickerFileList();
            }
        }

        ImGui.SameLine();
        using (var preview = ImRaii.Child("local-image-picker-preview", new Vector2(previewWidth, bodyHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (preview.Success)
            {
                this.DrawLocalImagePickerPreview();
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("File Name:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(this.Scale(220f), ImGui.GetContentRegionAvail().X - this.Scale(170f)));
        ImGui.InputText("##local-image-picker-file-name", ref this.localImagePickerFileName, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        ImGui.TextDisabled("*.png;*.jpg;*.jpeg");

        var okEnabled = !string.IsNullOrWhiteSpace(this.selectedLocalImagePath) && File.Exists(this.selectedLocalImagePath);
        var okWidth = this.Scale(138f);
        var cancelWidth = this.Scale(94f);
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - okWidth - cancelWidth - ImGui.GetStyle().ItemSpacing.X));
        using (ImRaii.Disabled(!okEnabled))
        {
            if (this.DrawPhonePillButton("Import and Apply", new Vector2(okWidth, this.Scale(32f))) && okEnabled && this.selectedLocalImagePath is not null)
            {
                this.ApplySelectedLocalImage(this.selectedLocalImagePath);
                ImGui.CloseCurrentPopup();
                return;
            }
        }

        ImGui.SameLine();
        if (this.DrawPhonePillButton("Cancel", new Vector2(cancelWidth, this.Scale(32f))))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawLocalImagePickerPreview()
    {
        ImGui.TextDisabled("Preview");
        ImGui.Separator();

        if (string.IsNullOrWhiteSpace(this.selectedLocalImagePath) || !File.Exists(this.selectedLocalImagePath))
        {
            ImGui.TextWrapped("Select an image to preview it before applying.");
            return;
        }

        var texture = this.appIconRenderer.TryGetTexture(this.selectedLocalImagePath);
        if (texture is null)
        {
            ImGui.TextWrapped("Loading preview...");
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        var previewSize = Math.Min(available.X, Math.Max(this.Scale(120f), available.Y - this.Scale(58f)));
        var imageSize = new Vector2(previewSize, previewSize);
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + imageSize, ImGui.GetColorU32(new Vector4(0.07f, 0.08f, 0.11f, 0.62f)), 0f);
        drawList.AddImage(texture.Handle, cursor, cursor + imageSize, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One));
        ImGui.Dummy(imageSize);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + this.Scale(6f));
        ImGui.TextWrapped(Path.GetFileName(this.selectedLocalImagePath));
    }

    private void DrawLocalImagePickerToolbar()
    {
        var parent = Directory.GetParent(this.localImagePickerDirectory);
        using (ImRaii.Disabled(parent is null))
        {
            if (this.DrawPhonePillButton("Up", new Vector2(this.Scale(42f), this.Scale(24f))) && parent is not null)
            {
                this.SetLocalImagePickerDirectory(parent.FullName);
            }
        }

        ImGui.SameLine();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home) && this.DrawPhonePillButton("Home", new Vector2(this.Scale(58f), this.Scale(24f))))
        {
            this.SetLocalImagePickerDirectory(home);
        }

        ImGui.SameLine();
        this.DrawLocalImagePickerBreadcrumbs();

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + this.Scale(3f));
        ImGui.TextDisabled("Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##local-image-picker-search", ref this.localImagePickerSearch, 120);
    }

    private void DrawLocalImagePickerBreadcrumbs()
    {
        var root = Path.GetPathRoot(this.localImagePickerDirectory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            if (this.DrawPhonePillButton(root.TrimEnd(Path.DirectorySeparatorChar), new Vector2(this.Scale(46f), this.Scale(24f))))
            {
                this.SetLocalImagePickerDirectory(root);
            }
        }

        var relative = !string.IsNullOrWhiteSpace(root)
            ? this.localImagePickerDirectory[root.Length..]
            : this.localImagePickerDirectory;
        var current = root ?? string.Empty;
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            ImGui.SameLine();
            var width = Math.Clamp(ImGui.CalcTextSize(part).X + this.Scale(18f), this.Scale(46f), this.Scale(96f));
            if (this.DrawPhonePillButton($"{part}##crumb-{current}", new Vector2(width, this.Scale(24f))))
            {
                this.SetLocalImagePickerDirectory(current);
            }
        }
    }

    private void DrawLocalImagePickerSidebar()
    {
        foreach (var shortcut in this.GetLocalImagePickerShortcuts())
        {
            if (Directory.Exists(shortcut.Path)
                && this.DrawPhonePillButton($"{shortcut.Label}##shortcut-{shortcut.Path}", new Vector2(-1f, this.Scale(24f))))
            {
                this.SetLocalImagePickerDirectory(shortcut.Path);
            }
        }
    }

    private IReadOnlyList<(string Label, string Path)> GetLocalImagePickerShortcuts()
    {
        var shortcuts = new List<(string Label, string Path)>
        {
            ("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        shortcuts.Insert(3, ("Downloads", downloads));

        foreach (var drive in DriveInfo.GetDrives().Where(item => item.IsReady).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            shortcuts.Add((drive.Name.TrimEnd(Path.DirectorySeparatorChar), drive.RootDirectory.FullName));
        }

        return shortcuts;
    }

    private void DrawLocalImagePickerFileList()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var typeX = Math.Max(this.Scale(250f), width - this.Scale(250f));
        var sizeX = Math.Max(typeX + this.Scale(62f), width - this.Scale(158f));
        var dateX = Math.Max(sizeX + this.Scale(54f), width - this.Scale(92f));
        ImGui.TextDisabled("File Name");
        ImGui.SameLine(typeX);
        ImGui.TextDisabled("Type");
        ImGui.SameLine(sizeX);
        ImGui.TextDisabled("Size");
        ImGui.SameLine(dateX);
        ImGui.TextDisabled("Date");
        ImGui.Separator();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(this.localImagePickerDirectory)
                         .Where(this.MatchesLocalImagePickerSearch)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (this.DrawLocalImagePickerRow($"[Folder] {name}", "Folder", string.Empty, Directory.GetLastWriteTime(directory).ToString("yyyy/MM/dd HH:mm"), false, directory, typeX, sizeX, dateX))
                {
                    this.SetLocalImagePickerDirectory(directory);
                }
            }

            foreach (var file in Directory.EnumerateFiles(this.localImagePickerDirectory)
                         .Where(IsSupportedWallpaperFile)
                         .Where(this.MatchesLocalImagePickerSearch)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                var selected = string.Equals(this.selectedLocalImagePath, file, StringComparison.OrdinalIgnoreCase);
                if (this.DrawLocalImagePickerRow(info.Name, Path.GetExtension(file).TrimStart('.').ToLowerInvariant(), this.FormatFileSize(info.Length), info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"), selected, file, typeX, sizeX, dateX))
                {
                    this.selectedLocalImagePath = file;
                    this.localImagePickerFileName = info.Name;
                }

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    this.ApplySelectedLocalImage(file);
                    ImGui.CloseCurrentPopup();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            this.pendingStatus = $"Could not browse folder: {this.SanitizeUserFacingError(ex.Message)}";
        }
    }

    private bool DrawLocalImagePickerRow(string name, string type, string size, string date, bool selected, string id, float typeX, float sizeX, float dateX)
    {
        var rowHeight = this.Scale(24f);
        var cursor = ImGui.GetCursorScreenPos();
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        ImGui.InvisibleButton($"##picker-row-{id}", new Vector2(contentWidth, rowHeight));

        var hovered = ImGui.IsItemHovered();
        if (selected || hovered)
        {
            var color = selected
                ? ImGui.GetColorU32(new Vector4(0.23f, 0.31f, 0.47f, 0.85f))
                : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f));
            drawList.AddRectFilled(cursor, cursor + new Vector2(contentWidth, rowHeight), color, 0f);
        }

        var textY = cursor.Y + Math.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f);
        var textColor = ImGui.GetColorU32(new Vector4(0.94f, 0.96f, 1f, 1f));
        var mutedColor = ImGui.GetColorU32(new Vector4(0.72f, 0.77f, 0.88f, 1f));
        drawList.AddText(new Vector2(cursor.X + this.Scale(6f), textY), textColor, name);
        drawList.AddText(new Vector2(cursor.X + typeX, textY), mutedColor, type);
        drawList.AddText(new Vector2(cursor.X + sizeX, textY), mutedColor, size);
        drawList.AddText(new Vector2(cursor.X + dateX, textY), mutedColor, date);

        return ImGui.IsItemClicked(ImGuiMouseButton.Left);
    }

    private bool MatchesLocalImagePickerSearch(string path)
    {
        return string.IsNullOrWhiteSpace(this.localImagePickerSearch)
            || Path.GetFileName(path).Contains(this.localImagePickerSearch, StringComparison.OrdinalIgnoreCase);
    }

    private void SetLocalImagePickerDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        this.localImagePickerDirectory = directory;
        this.selectedLocalImagePath = null;
        this.localImagePickerFileName = string.Empty;
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024f * 1024f * 1024f):0.0} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024f * 1024f):0.0} MB";
        }

        if (bytes >= 1024L)
        {
            return $"{bytes / 1024f:0.0} KB";
        }

        return $"{bytes} B";
    }

    private void ApplySelectedLocalImage(string path)
    {
        this.localImagePickerDirectory = Path.GetDirectoryName(path) ?? this.localImagePickerDirectory;
        switch (this.localImagePickerTarget)
        {
            case LocalImagePickerTarget.Wallpaper:
                this.TryImportBackgroundImage(path);
                break;
        }
    }

    private void SaveConfiguration()
    {
        this.service.PluginInterface.SavePluginConfig(this.configuration);
    }

    private void TryImportBackgroundImage(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                this.pendingStatus = "Choose a PNG or JPG wallpaper first";
                return;
            }

            var trimmed = sourcePath.Trim().Trim('"');
            var extension = Path.GetExtension(trimmed);
            if (!File.Exists(trimmed)
                || (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                this.pendingStatus = "Wallpaper must be a PNG or JPG file";
                return;
            }

            this.ImportBackgroundImage(trimmed);
        }
        catch (Exception ex)
        {
            this.pendingStatus = $"Wallpaper import failed: {this.SanitizeUserFacingError(ex.Message)}";
        }
    }

    private void ImportBackgroundImage(string sourcePath)
    {
        var destinationPath = this.configuration.GetLocalWallpaperImportPath(sourcePath);
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
        this.ApplyWallpaper(destinationPath);
    }

    private void ResetBackgroundImage()
    {
        this.appIconRenderer.Invalidate(this.configuration.BackgroundImagePath);
        this.configuration.BackgroundImagePath = string.Empty;
        this.configuration.BackgroundZoom = 1f;
        this.configuration.BackgroundOffsetX = 0f;
        this.configuration.BackgroundOffsetY = 0f;
        this.configuration.BackgroundMode = PhoneWallpaperMode.Fit;
        this.SaveConfiguration();
        this.pendingStatus = "Wallpaper reset";
    }

    private void DrawWallpaper(Vector2 screenMin, Vector2 screenMax, float rounding)
    {
        var draw = ImGui.GetWindowDrawList();
        var texture = this.appIconRenderer.TryGetTexture(this.configuration.BackgroundImagePath);
        var drewWallpaperImage = false;
        var viewport = screenMax - screenMin;
        if (texture is not null && viewport.X > 0f && viewport.Y > 0f)
        {
            var textureSize = new Vector2(texture.Width, texture.Height);
            if (textureSize.X > 0f && textureSize.Y > 0f)
            {
                var uvWidth = 1f;
                var uvHeight = 1f;
                if (this.configuration.BackgroundMode != PhoneWallpaperMode.Stretch)
                {
                    var viewportAspect = viewport.X / viewport.Y;
                    var textureAspect = textureSize.X / textureSize.Y;
                    if (textureAspect > viewportAspect)
                    {
                        uvWidth = viewportAspect / textureAspect;
                    }
                    else
                    {
                        uvHeight = textureAspect / viewportAspect;
                    }
                }

                var zoom = this.configuration.BackgroundMode == PhoneWallpaperMode.Custom
                    ? Math.Clamp(this.configuration.BackgroundZoom, 1f, 2.75f)
                    : 1f;
                uvWidth = Math.Clamp(uvWidth / zoom, 0.05f, 1f);
                uvHeight = Math.Clamp(uvHeight / zoom, 0.05f, 1f);
                var maxOffsetX = (1f - uvWidth) * 0.5f;
                var maxOffsetY = (1f - uvHeight) * 0.5f;
                var offsetX = this.configuration.BackgroundMode == PhoneWallpaperMode.Custom ? this.configuration.BackgroundOffsetX : 0f;
                var offsetY = this.configuration.BackgroundMode == PhoneWallpaperMode.Custom ? this.configuration.BackgroundOffsetY : 0f;
                var centerX = 0.5f + Math.Clamp(offsetX, -1f, 1f) * maxOffsetX;
                var centerY = 0.5f + Math.Clamp(offsetY, -1f, 1f) * maxOffsetY;
                var uv0 = new Vector2(centerX - uvWidth * 0.5f, centerY - uvHeight * 0.5f);
                var uv1 = new Vector2(centerX + uvWidth * 0.5f, centerY + uvHeight * 0.5f);

                draw.AddImageRounded(texture.Handle, screenMin, screenMax, uv0, uv1, ImGui.GetColorU32(Vector4.One), rounding);
                drewWallpaperImage = true;
            }
        }

        if (!drewWallpaperImage && this.configuration.UseSolidBackgroundColor)
        {
            var solid = this.ParseHexColor(this.configuration.SolidBackgroundColorHex, new Vector4(0.105f, 0.133f, 0.2f, 1f));
            solid.W = Math.Clamp(this.configuration.SolidBackgroundAlpha, 0f, 1f);
            draw.AddRectFilled(screenMin, screenMax, ImGui.GetColorU32(solid), rounding);
        }
    }

    private void DrawIconTintOverlay(ImDrawListPtr draw, Vector2 min, Vector2 max, float rounding)
    {
        if (!this.configuration.UseIconTint || this.configuration.IconTintAlpha <= 0f)
        {
            return;
        }

        var tint = this.ParseHexColor(this.configuration.IconTintColorHex, new Vector4(0.85f, 0.71f, 0.43f, 1f));
        tint.W = Math.Clamp(this.configuration.IconTintAlpha, 0f, 0.85f);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(tint), rounding);
    }

    private Vector4 ParseHexColor(string? value, Vector4 fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return fallback;
        }

        return new Vector4(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }

    private static string ToHexColor(float red, float green, float blue)
    {
        var r = (byte)Math.Clamp(MathF.Round(red), 0f, 255f);
        var g = (byte)Math.Clamp(MathF.Round(green), 0f, 255f);
        var b = (byte)Math.Clamp(MathF.Round(blue), 0f, 255f);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private void RefreshStaffDashboard()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation("staff-dashboard", () => this.client.GetAdminDashboardAsync(authToken), dashboard =>
        {
            this.adminDashboard = dashboard;
            this.pendingStatus = "Staff Console refreshed";
        }, "Refreshing Staff Console...");
    }

    private void OpenConversation(Guid conversationId, PhoneTab tab = PhoneTab.Messages)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.ResetConversationManagementState();
        this.selectedConversationId = conversationId;
        this.pendingStatus = "Opening conversation...";
        this.QueueUiOperation($"open-conversation-{conversationId}", async () =>
        {
            var messagesTask = this.client.GetConversationMessagesAsync(authToken, conversationId);
            var detailTask = this.client.GetConversationDetailAsync(authToken, conversationId);
            await Task.WhenAll(messagesTask, detailTask).ConfigureAwait(false);
            return (Messages: await messagesTask.ConfigureAwait(false), Detail: await detailTask.ConfigureAwait(false));
        }, result =>
        {
            if (this.selectedConversationId != conversationId)
            {
                return;
            }

            this.selectedConversationMessages = result.Messages;
            this.selectedConversationDetail = result.Detail;
            this.SyncMessageFolderForConversation(conversationId);
            this.renderedMessageCount = 0;
            this.scrollMessagesToBottom = true;
            this.showHomeScreen = false;
            this.activeTab = PhoneTab.Messages;
            this.DismissNotificationsFor(conversationId);
        }, "Opening conversation...", _ =>
        {
            if (this.selectedConversationId == conversationId)
            {
                this.ClearSelectedConversation();
            }
        });
    }

    private void OpenDirectConversation(string target)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken) || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        var normalizedTarget = target.Trim();
        this.QueueUiOperation($"open-direct-{normalizedTarget}",
            () => this.client.StartDirectConversationAsync(authToken, new StartDirectConversationRequest(normalizedTarget)),
            conversation => this.OpenConversation(conversation.Id),
            "Finding conversation...");
    }

    private void BeginDirectCall(string target)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken) || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        var normalizedTarget = target.Trim();
        this.QueueUiOperation($"call-direct-{normalizedTarget}",
            () => this.client.StartDirectConversationAsync(authToken, new StartDirectConversationRequest(normalizedTarget)),
            conversation => this.BeginConversationCall(conversation.Id, false),
            "Finding contact...");
    }

    private void ClearSelectedConversation()
    {
        this.selectedConversationId = null;
        this.selectedConversationMessages = null;
        this.selectedConversationDetail = null;
        this.pendingConversationMessagesTask = null;
        this.pendingConversationDetailTask = null;
        this.ResetConversationManagementState();
        this.renderedMessageCount = 0;
        this.scrollMessagesToBottom = true;
    }

    private void ResetConversationManagementState()
    {
        this.showGroupMembersWindow = false;
        this.groupAddTarget = string.Empty;
        this.pendingGroupRemoveMemberAccountId = null;
        this.pendingGroupRemoveMemberName = string.Empty;
        this.pendingConversationDeleteId = null;
        this.pendingConversationDeleteName = string.Empty;
        this.pendingConversationDeleteAction = null;
    }

    private void ApplyConversationModerationOutcome(Guid conversationId, ConversationDetail? updated, string successMessage)
    {
        if (updated is null)
        {
            this.state.Conversations.RemoveAll(item => item.Id == conversationId);
            if (this.adminDashboard is not null)
            {
                var tickets = this.adminDashboard.Tickets.Where(item => item.ConversationId != conversationId).ToList();
                this.adminDashboard = new AdminDashboardSnapshot(this.adminDashboard.Accounts, this.adminDashboard.Reports, this.adminDashboard.AuditLogs, tickets, this.adminDashboard.ActiveAnnouncement);
            }

            if (this.selectedConversationId == conversationId)
            {
                this.ClearSelectedConversation();
            }

            this.RefreshSnapshot();
            this.pendingStatus = successMessage;
            return;
        }

        if (this.selectedConversationId == conversationId)
        {
            this.selectedConversationDetail = updated;
            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.pendingConversationMessagesTask = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId);
            }

            this.renderedMessageCount = 0;
            this.scrollMessagesToBottom = true;
            this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
        }

        this.RefreshSnapshot();
        this.pendingStatus = successMessage;
    }

    private List<ContactRecord> GetEligibleGroupInviteContacts(ConversationDetail detail)
    {
        var activeMemberIds = detail.Members
            .Select(member => member.AccountId)
            .ToHashSet();
        var pendingTargetIds = detail.PendingMemberRequests
            .Select(item => item.TargetAccountId)
            .ToHashSet();

        return this.state.Contacts
            .Where(contact => contact.Id != this.state.CurrentProfile.AccountId)
            .Where(contact => !activeMemberIds.Contains(contact.Id))
            .Where(contact => !pendingTargetIds.Contains(contact.Id))
            .Where(contact => string.IsNullOrWhiteSpace(this.groupAddTarget)
                || contact.DisplayName.Contains(this.groupAddTarget, StringComparison.OrdinalIgnoreCase)
                || contact.PhoneNumber.Contains(this.groupAddTarget, StringComparison.OrdinalIgnoreCase))
            .OrderBy(contact => GetContactSortKey(contact.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(contact => contact.PhoneNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyGroupMemberAction(Guid conversationId, ChatModerationAction action, Guid targetAccountId, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        this.groupAddTarget = string.Empty;
        this.ModerateConversation(conversationId, action, targetAccountId, successMessage);
    }

    private void SendFriendRequest(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"friend-request-{phoneNumber}",
            () => this.client.CreateFriendRequestAsync(authToken, new FriendRequestCreateRequest(phoneNumber, null)),
            request =>
            {
                this.pendingStatus = request.Status == FriendRequestStatus.Accepted ? "Friend paired" : "Friend request sent";
                this.RefreshSnapshot();
            },
            "Sending friend request...");
    }

    private void BlockAccount(Guid accountId)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"block-account-{accountId}", () => this.client.BlockAccountAsync(authToken, accountId), success =>
        {
            this.pendingStatus = success ? "Blocked" : "Block failed";
            if (success)
            {
                this.RefreshSnapshot();
            }
        }, "Blocking account...");
    }

    private void ModerateConversation(Guid conversationId, ChatModerationAction action, Guid? targetAccountId, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"moderate-{conversationId}-{action}-{targetAccountId}",
            () => this.client.ModerateConversationAsync(authToken, new ConversationModerationRequest(conversationId, action, targetAccountId)),
            updated => this.ApplyConversationModerationOutcome(conversationId, updated, successMessage),
            "Updating conversation...");
    }

    private void OpenGroupMembersWindow(Guid conversationId)
    {
        if (this.selectedConversationDetail is not { IsGroup: true } detail)
        {
            this.groupMembersOverlayWindow.IsOpen = false;
            return;
        }

        this.showGroupMembersWindow = true;
        this.groupMembersOverlayWindow.ConversationId = conversationId;
        this.groupMembersOverlayWindow.WindowName = $"Members - {detail.Name}###group-members-{conversationId}";
        this.groupMembersOverlayWindow.IsOpen = true;
    }

    private void UpdateGroupMembersWindowState(Guid conversationId)
    {
        if (!this.showGroupMembersWindow || this.selectedConversationDetail is not { IsGroup: true } detail)
        {
            this.groupMembersOverlayWindow.IsOpen = false;
            return;
        }

        if (!this.groupMembersOverlayWindow.IsOpen)
        {
            this.showGroupMembersWindow = false;
            return;
        }

        this.groupMembersOverlayWindow.ConversationId = conversationId;
        this.groupMembersOverlayWindow.WindowName = $"Members - {detail.Name}###group-members-{conversationId}";
    }

    private void DrawGroupMembersWindowContent(Guid conversationId)
    {
        if (this.selectedConversationDetail is not { IsGroup: true } detail)
        {
            this.groupMembersOverlayWindow.IsOpen = false;
            this.showGroupMembersWindow = false;
            return;
        }

        var ownsConversation = detail.IsOwner;
        var isStandardGroup = detail.LinkedSupportTicketId is null;
        var allowRosterEdits = ownsConversation && detail.IsViewerActive && !detail.IsReadOnly && isStandardGroup;
        var allowMemberInviteRequests = !ownsConversation && detail.IsViewerActive && !detail.IsReadOnly && isStandardGroup;
        ImGui.TextDisabled(allowRosterEdits
            ? "Manage the roster for this group chat and its future group calls."
            : allowMemberInviteRequests
                ? "Pick one of your contacts and the owner can approve them for this group."
            : detail.IsReadOnly
                ? "This group is closed. The roster stays visible for reference."
                : "Only the group owner can change the roster.");
        ImGui.Separator();

            if (detail.PendingMemberRequests.Count > 0)
            {
                ImGui.TextDisabled("Pending");
                for (var index = 0; index < detail.PendingMemberRequests.Count; index++)
                {
                    var request = detail.PendingMemberRequests[index];
                    this.DrawCopyableText(request.TargetDisplayName, request.TargetDisplayName, "Name copied");
                    if (!string.IsNullOrWhiteSpace(request.TargetPhoneNumber))
                    {
                        this.DrawCopyableText(request.TargetPhoneNumber, request.TargetPhoneNumber, "Phone number copied", true);
                    }

                    this.DrawWrappedDisabledText($"Requested by {request.RequestedByDisplayName} {request.RequestedAtUtc.LocalDateTime:g}");
                    if (allowRosterEdits)
                    {
                        var actionSpacing = this.Scale(8f);
                        var actionWidth = Math.Max(this.Scale(96f), (ImGui.GetContentRegionAvail().X - actionSpacing) * 0.5f);
                        if (this.DrawPhonePillButton($"Approve##pending-member-{request.TargetAccountId}", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            this.ApplyGroupMemberAction(conversationId, ChatModerationAction.ApprovePendingMemberRequest, request.TargetAccountId, "Member approved");
                        }

                        ImGui.SameLine(0f, actionSpacing);
                        if (this.DrawPhonePillButton($"Decline##pending-member-{request.TargetAccountId}", new Vector2(actionWidth, this.Scale(30f))))
                        {
                            this.ApplyGroupMemberAction(conversationId, ChatModerationAction.DeclinePendingMemberRequest, request.TargetAccountId, "Request declined");
                        }
                    }
                    else if (request.RequestedByAccountId == this.state.CurrentProfile.AccountId)
                    {
                        this.DrawWrappedDisabledText("Awaiting owner approval");
                    }

                    if (index < detail.PendingMemberRequests.Count - 1)
                    {
                        ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
                    }
                }

                ImGui.Dummy(new Vector2(0f, this.Scale(8f)));
                ImGui.Separator();
            }

            if (allowRosterEdits || allowMemberInviteRequests)
            {
                var pickerButtonLabel = allowRosterEdits ? "Add From Contacts" : "Request From Contacts";
                var pickerButtonWidth = Math.Max(this.Scale(176f), ImGui.CalcTextSize(pickerButtonLabel).X + this.Scale(28f));
                if (this.DrawPhonePillButton(pickerButtonLabel, new Vector2(pickerButtonWidth, this.Scale(32f))))
                {
                    this.groupAddTarget = string.Empty;
                    ImGui.OpenPopup($"group-contact-picker##{conversationId}");
                }

                ImGui.SetNextWindowPos(this.GetPhoneWindowCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowSize(this.Scale(420f, 374f), ImGuiCond.Always);
                using var groupContactPickerPopup = ImRaii.Popup($"group-contact-picker##{conversationId}", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                if (groupContactPickerPopup.Success)
                {
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint("##group-contact-picker-filter", "Search contacts", ref this.groupAddTarget, 64);
                    ImGui.Separator();
                    var eligibleContacts = this.GetEligibleGroupInviteContacts(detail);
                    using var pickerList = ImRaii.Child("group-contact-picker-list", new Vector2(-1f, this.Scale(280f)), true);
                    if (pickerList.Success)
                    {
                        if (eligibleContacts.Count == 0)
                        {
                            ImGui.TextDisabled("No eligible contacts to add right now.");
                        }
                        else
                        {
                            foreach (var contact in eligibleContacts)
                            {
                                ImGui.TextUnformatted(contact.DisplayName);
                                ImGui.TextDisabled(contact.PhoneNumber);
                                var actionLabel = allowRosterEdits ? "Add" : "Request";
                                var actionWidth = Math.Max(this.Scale(92f), ImGui.CalcTextSize(actionLabel).X + this.Scale(28f));
                                var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - actionWidth);
                                ImGui.SetCursorPosX(maxX);
                                if (this.DrawPhonePillButton($"{actionLabel}##group-contact-{contact.Id}", new Vector2(actionWidth, this.Scale(28f))))
                                {
                                    var action = allowRosterEdits ? ChatModerationAction.AddMember : ChatModerationAction.RequestAddMember;
                                    var successMessage = allowRosterEdits ? "Member added" : "Request sent to the owner";
                                    this.ApplyGroupMemberAction(conversationId, action, contact.Id, successMessage);
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.Separator();
                            }
                        }
                    }
                }

                if (allowRosterEdits)
                {
                    ImGui.Spacing();
                    ImGui.TextDisabled("Owner Controls");
                    ImGui.TextWrapped("Close keeps the group readable but turns messaging and calls off for everyone. Delete from the chat list removes it from every member while keeping server moderation records.");
                    if (this.DrawPhonePillButton("Close Group Chat", this.Scale(156f, 30f)))
                    {
                        ImGui.OpenPopup($"Confirm?###confirm-close-group-chat##{conversationId}");
                    }
                }
            }
            else if (detail.IsReadOnly)
            {
                ImGui.TextDisabled("Closed groups stay readable, but roster changes, messages, and calls are locked.");
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Members");
            using var memberList = ImRaii.Child("group-members-popup-list", new Vector2(-1f, this.Scale(250f)), true);
            if (memberList.Success)
            {
                foreach (var member in detail.Members.OrderByDescending(item => item.Role).ThenBy(item => item.DisplayName))
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
                        var actionWidth = allowRosterEdits ? this.Scale(76f) : this.Scale(84f);
                        var maxX = Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - actionWidth);
                        if (allowRosterEdits)
                        {
                            maxX = Math.Max(maxX, ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - this.Scale(112f));
                        }

                        ImGui.SetCursorPosX(maxX);
                        if (ImGui.Button($"Friend##member-{member.AccountId}", new Vector2(actionWidth, this.Scale(26f))))
                        {
                            this.SendFriendRequest(member.PhoneNumber);
                        }

                        if (allowRosterEdits)
                        {
                            ImGui.SameLine();
                            using var deleteColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.48f, 0.17f, 0.2f, 0.9f));
                            using var deleteHover = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.61f, 0.21f, 0.24f, 0.95f));
                            using var deleteActive = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.38f, 0.12f, 0.14f, 1f));
                            if (ImGui.Button($"Remove##member-{member.AccountId}", new Vector2(this.Scale(92f), this.Scale(26f))))
                            {
                                this.pendingGroupRemoveMemberAccountId = member.AccountId;
                                this.pendingGroupRemoveMemberName = member.DisplayName;
                                ImGui.OpenPopup($"Confirm?###confirm-remove-group-member##{conversationId}");
                            }
                        }
                    }

                    ImGui.Separator();
                }
            }

            var removeGroupMemberWarning = $"Remove {this.pendingGroupRemoveMemberName} from this group? They will keep the earlier log, but they will stop seeing new messages and calls.";
            this.PrepareConfirmModal(removeGroupMemberWarning, "Remove", this.Scale(110f, 30f));
            using var removeGroupMemberWindowRounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f);
            using var removeGroupMemberPopupRounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 0f);
            using var removeGroupMemberTitlePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, this.Scale(10f, 3f));
            using var removeGroupMemberPopup = ImRaii.PopupModal($"Confirm?###confirm-remove-group-member##{conversationId}", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (removeGroupMemberPopup.Success)
            {
                this.DrawConfirmModalText(removeGroupMemberWarning);
                if (ImGui.Button("Cancel", this.Scale(110f, 30f)))
                {
                    this.pendingGroupRemoveMemberAccountId = null;
                    this.pendingGroupRemoveMemberName = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove", this.Scale(110f, 30f)) && this.pendingGroupRemoveMemberAccountId is Guid removeId && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.ModerateConversation(conversationId, ChatModerationAction.RemoveMember, removeId, "Member removed");

                    this.pendingGroupRemoveMemberAccountId = null;
                    this.pendingGroupRemoveMemberName = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
            }

            var closeGroupChatWarning = "Close this group chat for everyone? The history stays readable, but new messages and calls will stop.";
            this.PrepareConfirmModal(closeGroupChatWarning, "Close Chat", this.Scale(120f, 30f));
            using var closeGroupChatWindowRounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f);
            using var closeGroupChatPopupRounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 0f);
            using var closeGroupChatTitlePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, this.Scale(10f, 3f));
            using var closeGroupChatPopup = ImRaii.PopupModal($"Confirm?###confirm-close-group-chat##{conversationId}", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (closeGroupChatPopup.Success)
            {
                this.DrawConfirmModalText(closeGroupChatWarning);
                if (ImGui.Button("Cancel", this.Scale(110f, 30f)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Close Chat", this.Scale(120f, 30f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.ModerateConversation(conversationId, ChatModerationAction.CloseConversation, null, "Group chat closed");

                    ImGui.CloseCurrentPopup();
                }
            }
    }

    private string GetSelectedConversationStatusLine()
    {
        if (this.selectedConversationDetail is not { } detail)
        {
            return string.Empty;
        }

        if (!detail.IsViewerActive)
        {
            return "You were removed from this group. Earlier messages stay visible, but everything newer is locked.";
        }

        if (detail.IsReadOnly && detail.LinkedSupportTicketId is not null)
        {
            return "This ticket is closed. Staff and participants can still review the log.";
        }

        if (detail.IsReadOnly)
        {
            return "This conversation is closed. Everyone can still read it, but new messages and calls are off.";
        }

        return string.Empty;
    }

    private float GetSelectedConversationDetailHeight()
    {
        if (this.selectedConversationDetail is not { } detail)
        {
            return 0f;
        }

        if (detail.LinkedSupportTicketId is not null)
        {
            return this.IsCurrentUserStaff()
                ? this.Scale(136f)
                : this.Scale(78f);
        }

        if (detail.IsGroup)
        {
            return this.Scale(116f);
        }

        return this.Scale(64f);
    }

    private string GetSelectedConversationComposerMessage()
    {
        if (this.selectedConversationDetail is not { } detail)
        {
            return "Messaging is unavailable right now.";
        }

        if (!detail.IsViewerActive)
        {
            return "You were removed from this group. Earlier messages stay visible, but you cannot send or receive anything newer here.";
        }

        if (detail.LinkedSupportTicketId is not null)
        {
            return "This ticket is closed. You can still read the log, but no new messages can be sent.";
        }

        return "This conversation is closed. You can still read the log, but no new messages or calls can be started.";
    }

    private string GetPendingConversationDeleteWarning()
    {
        return this.pendingConversationDeleteAction switch
        {
            ChatModerationAction.DeleteConversation => $"Delete {this.pendingConversationDeleteName} for everyone? The group will disappear from every member's phone, nobody will be able to send again, and anyone who wants back later would need a fresh invite. Server moderation records are kept.",
            ChatModerationAction.LeaveConversation => $"Leave {this.pendingConversationDeleteName}? You will lose access to this group and will need a fresh invite before you can see it again.",
            ChatModerationAction.HideConversation => $"Remove {this.pendingConversationDeleteName} from your chat list? You will stop seeing this conversation here until it is reopened again.",
            _ => "Remove this conversation?"
        };
    }

    private string GetPendingConversationDeleteConfirmLabel()
    {
        return this.pendingConversationDeleteAction switch
        {
            ChatModerationAction.DeleteConversation => "Delete Group",
            ChatModerationAction.LeaveConversation => "Leave Group",
            ChatModerationAction.HideConversation => "Remove Chat",
            _ => "Confirm"
        };
    }

    private bool IsCurrentUserStaff()
    {
        return this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator;
    }

    private void AddSupportTicketParticipant(Guid ticketId, string rawTarget, bool openConversation)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken) || string.IsNullOrWhiteSpace(rawTarget))
        {
            this.pendingStatus = "Enter a person first";
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"ticket-add-{ticketId}", async () =>
        {
            var targetAccountId = (await this.ResolveConversationTargetsAsync(authToken, rawTarget).ConfigureAwait(false)).FirstOrDefault();
            if (targetAccountId == Guid.Empty)
            {
                throw new InvalidOperationException("Person could not be resolved");
            }

            return await this.client.AddSupportTicketParticipantAsync(authToken, ticketId, targetAccountId).ConfigureAwait(false);
        }, updated =>
        {
            if (updated is null)
            {
                this.pendingStatus = "Could not add participant";
                return;
            }

            this.UpsertSupportTicket(updated);
            this.staffTicketParticipantTarget = string.Empty;
            if (openConversation)
            {
                this.OpenConversation(updated.ConversationId);
            }
            this.RefreshSnapshot();
            this.RefreshStaffDashboard();
            this.pendingStatus = "Participant added";
        }, "Adding participant...");
    }

    private void CloseSupportTicket(Guid ticketId, bool openConversation)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"ticket-close-{ticketId}", () => this.client.CloseSupportTicketAsync(authToken, ticketId), updated =>
        {
            if (updated is null)
            {
                this.pendingStatus = "Could not close ticket";
                return;
            }

            this.UpsertSupportTicket(updated);
            if (openConversation)
            {
                this.OpenConversation(updated.ConversationId);
            }
            this.RefreshSnapshot();
            this.RefreshStaffDashboard();
            this.pendingStatus = "Ticket closed";
        }, "Closing ticket...");
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
        if (activeCall is null || this.pendingUiOperations.Any(item => string.Equals(item.Key, "call-transition", StringComparison.Ordinal)))
        {
            return;
        }

        var wasGroup = activeCall.IsGroup;
        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation("call-transition", async () =>
        {
            CallSummary? summary = null;
            Exception? serverError = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(authToken))
                {
                    summary = await this.client.EndActiveCallAsync(authToken, activeCall.SessionId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                serverError = ex;
            }
            finally
            {
                await this.voiceChatSession.StopAsync().ConfigureAwait(false);
            }

            if (serverError is not null)
            {
                throw serverError;
            }

            return summary;
        }, summary =>
        {
            if (summary is not null)
            {
                this.UpsertRecentCall(summary);
            }
            this.RefreshSnapshot();
        }, statusMessage ?? (wasGroup ? "Leaving call..." : "Ending call..."));
        this.state.ActiveCall = null;
        this.DismissIncomingCallNotifications();
        this.lastActiveCallRefreshUtc = DateTimeOffset.MinValue;
        this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
        this.pendingStatus = statusMessage ?? (wasGroup ? "Left call" : "Call ended");
    }

    private void ConnectVoiceToCurrentCall()
    {
        var activeCall = this.state.ActiveCall;
        if (activeCall is null || activeCall.IsIncoming || activeCall.VoiceSession is null || string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var inputResolution = VoiceAudioDeviceCatalog.ResolveInputDevice(this.configuration.PreferredVoiceInputDeviceKey, this.configuration.PreferredVoiceInputDeviceName);
        var outputResolution = VoiceAudioDeviceCatalog.ResolveOutputDevice(this.configuration.PreferredVoiceOutputDeviceKey, this.configuration.PreferredVoiceOutputDeviceName);
        this.QueueUiOperation("call-transition", async () =>
        {
            await this.voiceChatSession.StartAsync(
                this.configuration.ServerBaseUrl,
                this.configuration.AuthToken,
                this.state.CurrentProfile.AccountId,
                activeCall,
                inputResolution.DeviceNumber,
                outputResolution.DeviceNumber,
                this.configuration.ReduceVoiceBackgroundNoise,
                this.configuration.VoiceMicVolume,
                this.configuration.VoiceOutputVolume,
                this.lifetimeCancellation.Token).ConfigureAwait(false);
            return true;
        }, _ =>
        {
            this.voiceChatSession.SetMuted(activeCall.IsMuted);
            var fallbackMessage = GetVoiceDeviceFallbackMessage(inputResolution, outputResolution);
            if (!string.IsNullOrWhiteSpace(fallbackMessage))
            {
                this.pendingStatus = fallbackMessage;
            }
        }, "Connecting voice...");
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

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation("acknowledge-missed-calls", () => this.client.AcknowledgeMissedCallsAsync(authToken), count =>
        {
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
        }, "Clearing missed calls...");
    }
    private void BeginConversationCall(Guid conversationId, bool isGroup)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken)
            || this.pendingUiOperations.Any(item => string.Equals(item.Key, "call-transition", StringComparison.Ordinal)))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        var previousCall = this.state.ActiveCall is { ConversationId: var activeConversationId } && activeConversationId != conversationId
            ? this.state.ActiveCall
            : null;
        this.QueueUiOperation("call-transition", async () =>
        {
            if (previousCall is not null)
            {
                Exception? endError = null;
                try
                {
                    await this.client.EndActiveCallAsync(authToken, previousCall.SessionId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    endError = ex;
                }
                finally
                {
                    await this.voiceChatSession.StopAsync().ConfigureAwait(false);
                }

                if (endError is not null)
                {
                    throw endError;
                }
            }
            return await this.client.StartOrJoinActiveCallAsync(authToken, new StartCallRequest(conversationId, isGroup)).ConfigureAwait(false);
        }, session =>
        {
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
        }, previousCall is null ? "Starting call..." : "Switching calls...");
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

        var authToken = this.configuration.AuthToken;
        var identity = this.GetCurrentGameIdentity();
        var submittedComposeMessage = this.composeMessage;
        var submittedEmbedUrl = this.composeEmbedUrl;
        this.QueueUiOperation($"send-message-{conversationId}",
            () => this.client.SendMessageAsync(authToken, new SendMessageRequest(conversationId, body, identity, embeds)),
            sent =>
        {
            this.RecordConversationActivity(conversationId, sent.SentAtUtc);
            if (this.selectedConversationId != conversationId)
            {
                this.RefreshSnapshot(true);
                return;
            }

            this.selectedConversationMessages = new ConversationMessagePage(conversationId, this.selectedConversationMessages?.Messages.Append(sent).ToList() ?? [sent]);
            if (string.Equals(this.composeMessage, submittedComposeMessage, StringComparison.Ordinal)
                && string.Equals(this.composeEmbedUrl, submittedEmbedUrl, StringComparison.Ordinal))
            {
                this.composeMessage = string.Empty;
                this.composeEmbedUrl = string.Empty;
                this.composeControlVersion++;
                this.clearComposeOnNextDraw = true;
                this.focusComposeOnNextDraw = true;
            }
            this.scrollMessagesToBottom = true;
            this.lastConversationRefreshUtc = DateTimeOffset.MinValue;
            if (this.pendingConversationMessagesTask is null)
            {
                this.pendingConversationMessagesTask = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversationId);
            }
        }, "Sending message...");
    }

    private void SendGif(Guid conversationId, GiphyGifResult gif)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        var identity = this.GetCurrentGameIdentity();
        this.QueueUiOperation($"send-gif-{conversationId}", () => this.client.SendMessageAsync(
            authToken,
            new SendMessageRequest(
                conversationId,
                string.Empty,
                identity,
                [new SendMessageEmbedRequest(gif.GifUrl)])), sent =>
        {
            this.RecordConversationActivity(conversationId, sent.SentAtUtc);
            if (this.selectedConversationId != conversationId)
            {
                this.RefreshSnapshot(true);
                return;
            }

            this.selectedConversationMessages = new ConversationMessagePage(conversationId, this.selectedConversationMessages?.Messages.Append(sent).ToList() ?? [sent]);
            this.scrollMessagesToBottom = true;
            this.pendingStatus = "GIF sent";
        }, "Sending GIF...");
    }

    private void DrawGifPicker(Guid conversationId)
    {
        if (this.openGifPicker)
        {
            this.openGifPicker = false;
            ImGui.OpenPopup("KLIPY GIF Picker");
            var apiKey = this.GetKlipyApiKey();
            if (!string.IsNullOrWhiteSpace(apiKey) && this.pendingGifSearchTask is null && this.gifSearchResults.Count == 0)
            {
                this.pendingGifSearchTask = this.giphyClient.GetTrendingAsync(apiKey, this.configuration.GiphyRating, 24);
            }
        }

        ImGui.SetNextWindowSize(new Vector2(this.Scale(330f), this.Scale(430f)), ImGuiCond.Appearing);
        using var popup = ImRaii.PopupModal("KLIPY GIF Picker", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!popup.Success)
        {
            return;
        }

        var key = this.GetKlipyApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            ImGui.TextWrapped("Add a KLIPY API key in Settings before searching GIFs.");
            if (this.DrawPhonePillButton("Get KLIPY Key", new Vector2(-1f, this.Scale(32f))))
            {
                this.pendingExternalUrl = KlipyCreateAppUrl;
                this.showLinkWarningModal = true;
            }
            if (this.DrawPhonePillButton("Close", new Vector2(-1f, this.Scale(32f))))
            {
                ImGui.CloseCurrentPopup();
            }
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        var submittedWithEnter = ImGui.InputTextWithHint("##klipy-search", "Search KLIPY", ref this.gifSearchQuery, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        var submitSearch = submittedWithEnter || this.DrawPhonePillButton("Search", new Vector2(-1f, this.Scale(30f)));
        if (submitSearch && !string.IsNullOrWhiteSpace(this.gifSearchQuery) && this.pendingGifSearchTask is null)
        {
            this.pendingGifSearchTask = this.giphyClient.SearchAsync(key, this.gifSearchQuery.Trim(), this.configuration.GiphyRating, 24);
        }

        if (this.pendingGifSearchTask is { IsCompleted: true })
        {
            try
            {
                this.gifSearchResults = this.pendingGifSearchTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.pendingStatus = $"KLIPY search failed: {this.SanitizeUserFacingError(ex.Message)}";
                this.gifSearchResults = [];
            }
            this.pendingGifSearchTask = null;
        }

        ImGui.TextDisabled(this.pendingGifSearchTask is null ? "Powered by KLIPY" : "Loading KLIPY...");
        using var results = ImRaii.Child("klipy-results", new Vector2(-1f, this.Scale(300f)), true);
        if (results.Success)
        {
            foreach (var gif in this.gifSearchResults)
            {
                var title = string.IsNullOrWhiteSpace(gif.Title) ? "GIF" : gif.Title;
                if (this.DrawPhonePillButton($"{title}##klipy-{gif.GifId}", new Vector2(-1f, this.Scale(34f))))
                {
                    this.SendGif(conversationId, gif);
                    ImGui.CloseCurrentPopup();
                    break;
                }
            }
        }

        if (this.DrawPhonePillButton("Close", new Vector2(-1f, this.Scale(30f))))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private string GetKlipyApiKey()
    {
        return this.configuration.KlipyApiKey;
    }

    private bool DrawEditableText(string label, string value, Action<string> setter, int maxLength)
    {
        ImGui.TextDisabled(label);
        var buffer = value;
        if (ImGui.InputText($"##{label}", ref buffer, maxLength))
        {
            setter(buffer);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            return true;
        }

        return false;
    }

    private bool DrawPhonePillButton(string label, Vector2 size)
    {
        var clicked = ImGui.Button($"##phone-pill-{label}", size);
        var visibleLabel = GetVisibleButtonLabel(label);
        if (string.IsNullOrEmpty(visibleLabel))
        {
            return clicked;
        }

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var itemSize = itemMax - itemMin;
        var textSize = ImGui.CalcTextSize(visibleLabel);
        var textPosition = new Vector2(
            itemMin.X + Math.Max(0f, (itemSize.X - textSize.X) * 0.5f),
            itemMin.Y + Math.Max(0f, (itemSize.Y - textSize.Y) * 0.5f));

        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(itemMin, itemMax, true);
        drawList.AddText(textPosition, ImGui.GetColorU32(ImGuiCol.Text), visibleLabel);
        drawList.PopClipRect();
        return clicked;
    }

    private void DrawOutlinedText(ImDrawListPtr drawList, ImFontPtr font, float fontSize, Vector2 position, string text)
    {
        var outlineColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.86f));
        var textColor = ImGui.GetColorU32(Vector4.One);
        var offset = Math.Max(1f, this.Scale(1f));
        drawList.AddText(font, fontSize, position + new Vector2(-offset, 0f), outlineColor, text);
        drawList.AddText(font, fontSize, position + new Vector2(offset, 0f), outlineColor, text);
        drawList.AddText(font, fontSize, position + new Vector2(0f, -offset), outlineColor, text);
        drawList.AddText(font, fontSize, position + new Vector2(0f, offset), outlineColor, text);
        drawList.AddText(font, fontSize, position + new Vector2(-offset, -offset), outlineColor, text);
        drawList.AddText(font, fontSize, position + new Vector2(offset, offset), outlineColor, text);
        drawList.AddText(font, fontSize, position, textColor, text);
    }

    private static string GetVisibleButtonLabel(string label)
    {
        var hiddenLabelIndex = label.IndexOf("##", StringComparison.Ordinal);
        return hiddenLabelIndex >= 0
            ? label[..hiddenLabelIndex]
            : label;
    }

    private static int CountTextLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var lineCount = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lineCount++;
            }
        }

        return Math.Max(1, lineCount);
    }

    private string WrapDraftMessageText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= this.Scale(24f))
        {
            return text;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var paragraphs = normalized.Split('\n');
        for (var index = 0; index < paragraphs.Length; index++)
        {
            paragraphs[index] = this.WrapDraftParagraph(paragraphs[index], maxWidth);
        }

        return string.Join('\n', paragraphs);
    }

    private string WrapDraftParagraph(string paragraph, float maxWidth)
    {
        if (string.IsNullOrEmpty(paragraph))
        {
            return string.Empty;
        }

        var wrapped = new StringBuilder(paragraph.Length + 8);
        var currentLine = new StringBuilder();
        foreach (var character in paragraph)
        {
            currentLine.Append(character);
            if (ImGui.CalcTextSize(currentLine.ToString()).X <= maxWidth)
            {
                continue;
            }

            if (currentLine.Length == 1)
            {
                wrapped.Append(currentLine);
                currentLine.Clear();
                continue;
            }

            var breakIndex = FindLastWrapWhitespaceIndex(currentLine);
            if (breakIndex <= 0)
            {
                var overflowCharacter = currentLine[currentLine.Length - 1];
                currentLine.Length--;
                wrapped.Append(currentLine);
                wrapped.Append('\n');
                currentLine.Clear();
                currentLine.Append(overflowCharacter);
                continue;
            }

            var remainder = currentLine.ToString(breakIndex + 1, currentLine.Length - breakIndex - 1).TrimStart();
            wrapped.Append(currentLine.ToString(0, breakIndex).TrimEnd());
            wrapped.Append('\n');
            currentLine.Clear();
            currentLine.Append(remainder);
        }

        wrapped.Append(currentLine);
        return wrapped.ToString();
    }

    private static int FindLastWrapWhitespaceIndex(StringBuilder text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private void DrawSpellCheckOverlay(string fieldKey, ref string text, Action onReplacementApplied)
    {
        if (!this.configuration.EnableSpellCheck || string.IsNullOrWhiteSpace(text) || !this.spellCheckService.IsAvailable)
        {
            return;
        }

        var analysis = this.GetSpellCheckAnalysis(fieldKey, text);
        if (analysis.Issues.Count == 0)
        {
            return;
        }

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var style = ImGui.GetStyle();
        var itemHovered = ImGui.IsItemHovered();
        var mousePosition = ImGui.GetMousePos();
        SpellCheckIssue? hoveredIssue = null;
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(itemMin, itemMax, true);

        foreach (var issue in analysis.Issues)
        {
            if (!TryGetSpellIssueBounds(text, issue, itemMin, style.FramePadding, out var issueMin, out var issueMax))
            {
                continue;
            }

            this.DrawSpellUnderline(issueMin, issueMax);
            if (itemHovered && IsPointInsideRect(mousePosition, issueMin, issueMax))
            {
                hoveredIssue = issue;
            }
        }

        drawList.PopClipRect();

        if (hoveredIssue is not null)
        {
            using (var tooltip = ImRaii.Tooltip())
            {
                ImGui.TextUnformatted($"Spelling suggestions for \"{hoveredIssue.OriginalText}\"");
                ImGui.TextDisabled("Click or right-click the underline to choose a replacement.");
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                this.spellPopupFieldKey = fieldKey;
                this.spellPopupIssue = hoveredIssue;
                this.spellPopupPosition = mousePosition;
                ImGui.OpenPopup(GetSpellPopupId(fieldKey));
            }
        }

        this.DrawSpellSuggestionPopup(fieldKey, ref text, onReplacementApplied);
    }

    private SpellCheckAnalysis GetSpellCheckAnalysis(string fieldKey, string text)
    {
        if (!this.spellCheckFieldStates.TryGetValue(fieldKey, out var state))
        {
            state = new SpellFieldState();
            this.spellCheckFieldStates[fieldKey] = state;
        }

        if (!string.Equals(state.Text, text, StringComparison.Ordinal))
        {
            state.Text = text;
            state.Analysis = this.spellCheckService.Analyze(text);
        }

        return state.Analysis;
    }

    private void DrawSpellSuggestionPopup(string fieldKey, ref string text, Action onReplacementApplied)
    {
        if (!string.Equals(this.spellPopupFieldKey, fieldKey, StringComparison.Ordinal) || this.spellPopupIssue is null)
        {
            return;
        }

        var popupId = GetSpellPopupId(fieldKey);
        ImGui.SetNextWindowPos(this.spellPopupPosition, ImGuiCond.Appearing);
        using var popup = ImRaii.Popup(popupId);
        if (!popup.Success)
        {
            if (!ImGui.IsPopupOpen(popupId))
            {
                this.ClearSpellPopupState();
            }

            return;
        }

        var issue = this.spellPopupIssue;
        ImGui.TextDisabled(issue.OriginalText);
        ImGui.Separator();
        if (!IsSpellIssueStillCurrent(text, issue))
        {
            ImGui.TextDisabled("Text changed. Click the word again for fresh suggestions.");
        }
        else
        {
            foreach (var suggestion in issue.Suggestions)
            {
                if (ImGui.Selectable(suggestion.Text))
                {
                    text = text.Remove(issue.StartIndex, issue.Length).Insert(issue.StartIndex, suggestion.Text);
                    onReplacementApplied();
                    this.pendingStatus = $"Replaced \"{issue.OriginalText}\" with \"{suggestion.Text}\"";
                    this.ClearSpellPopupState();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        if (ImGui.Selectable("Keep original"))
        {
            this.ClearSpellPopupState();
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawSpellUnderline(Vector2 issueMin, Vector2 issueMax)
    {
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(new Vector4(0.90f, 0.23f, 0.23f, 1f));
        var baseY = issueMax.Y - this.Scale(2f);
        var segmentWidth = this.Scale(6f);
        var amplitude = this.Scale(2f);
        for (var x = issueMin.X; x < issueMax.X; x += segmentWidth)
        {
            var midX = Math.Min(x + (segmentWidth * 0.5f), issueMax.X);
            var nextX = Math.Min(x + segmentWidth, issueMax.X);
            drawList.AddLine(new Vector2(x, baseY), new Vector2(midX, baseY + amplitude), color, 1.35f);
            if (midX < issueMax.X)
            {
                drawList.AddLine(new Vector2(midX, baseY + amplitude), new Vector2(nextX, baseY), color, 1.35f);
            }
        }
    }

    private bool TryGetSpellIssueBounds(string text, SpellCheckIssue issue, Vector2 itemMin, Vector2 framePadding, out Vector2 issueMin, out Vector2 issueMax)
    {
        issueMin = default;
        issueMax = default;

        if (issue.StartIndex < 0 || issue.StartIndex + issue.Length > text.Length)
        {
            return false;
        }

        var lineIndex = 0;
        var lineStartIndex = 0;
        for (var index = 0; index < issue.StartIndex; index++)
        {
            if (text[index] == '\n')
            {
                lineIndex++;
                lineStartIndex = index + 1;
            }
        }

        var prefix = text[lineStartIndex..issue.StartIndex];
        var issueText = text.Substring(issue.StartIndex, issue.Length);
        var lineHeight = ImGui.GetTextLineHeight();
        var issueX = itemMin.X + framePadding.X + ImGui.CalcTextSize(prefix).X;
        var issueY = itemMin.Y + framePadding.Y + (lineIndex * lineHeight);
        var issueWidth = Math.Max(1f, ImGui.CalcTextSize(issueText).X);
        issueMin = new Vector2(issueX, issueY);
        issueMax = new Vector2(issueX + issueWidth, issueY + lineHeight);
        return true;
    }

    private static bool IsPointInsideRect(Vector2 point, Vector2 rectMin, Vector2 rectMax)
    {
        return point.X >= rectMin.X &&
               point.X <= rectMax.X &&
               point.Y >= rectMin.Y &&
               point.Y <= rectMax.Y;
    }

    private static string GetSpellPopupId(string fieldKey)
    {
        return $"TomestonePhoneSpellPopup##{fieldKey}";
    }

    private static bool IsSpellIssueStillCurrent(string text, SpellCheckIssue issue)
    {
        return issue.StartIndex >= 0 &&
               issue.StartIndex + issue.Length <= text.Length &&
               string.Equals(text.Substring(issue.StartIndex, issue.Length), issue.OriginalText, StringComparison.Ordinal);
    }

    private void ClearSpellPopupState()
    {
        this.spellPopupFieldKey = null;
        this.spellPopupIssue = null;
        this.spellPopupPosition = default;
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

    private void SyncGameIdentityPreference()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var identity = this.GetCurrentGameIdentity();
        var authToken = this.configuration.AuthToken;
        var request = identity is null
            ? new UpdateGameIdentityRequest(string.Empty, string.Empty)
            : new UpdateGameIdentityRequest(identity.CharacterName, identity.WorldName);
        this.QueueUiOperation("game-identity-update", () => this.client.UpdateGameIdentityAsync(authToken, request), profile =>
        {
            this.state.CurrentProfile = profile;
            this.pendingStatus = identity is null
                ? "Character/world sharing disabled"
                : "Character/world sharing enabled";
            this.RefreshSnapshot(true);
        }, "Updating character sharing...");
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
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false })
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var refreshInterval = this.IsOpen && !this.showHomeScreen && (this.activeTab is PhoneTab.Friends or PhoneTab.Contacts)
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(6);
        if (now - this.lastSnapshotRefreshUtc < refreshInterval)
        {
            return;
        }

        this.lastSnapshotRefreshUtc = now;
        this.RefreshSnapshot(true);
    }

    private void TickHeartbeat()
    {
        if (!this.HasHydratedAuthenticatedProfile()
            || this.pendingUiOperations.Any(item => string.Equals(item.Key, "heartbeat", StringComparison.Ordinal)))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - this.lastHeartbeatUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        this.lastHeartbeatUtc = now;
        var authToken = this.configuration.AuthToken!;
        this.QueueUiOperation("heartbeat", () => this.client.HeartbeatAsync(authToken), _ => { }, null);
    }

    private void TickActiveCallAutoRefresh()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
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
            if (this.pendingUiOperations.Any(item => string.Equals(item.Key, "call-transition", StringComparison.Ordinal)))
            {
                return;
            }

            this.QueueUiOperation("call-transition", async () =>
            {
                await this.voiceChatSession.StopAsync().ConfigureAwait(false);
                return true;
            }, _ => { }, "Call ended");
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
        var hitSize = new Vector2(this.Scale(236f), this.Scale(24f));
        var visualSize = new Vector2(this.Scale(208f), this.Scale(14f));
        var cursor = new Vector2(
            Math.Max(0f, (available.X - hitSize.X) * 0.5f),
            Math.Max(0f, (available.Y - hitSize.Y) * 0.5f));
        ImGui.SetCursorPos(cursor);
        using var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, hitSize.Y * 0.5f);
        using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.01f));
        using var buttonHovered = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.06f));
        using var buttonActive = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.1f));
        var hitPos = ImGui.GetCursorScreenPos();
        if (ImGui.Button("##Home", hitSize))
        {
            this.ClearSelectedConversation();

            if (!string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.showHomeScreen = true;
                this.refreshStaffDashboardOnOpen = true;
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
        draw.AddRectFilled(visualPos + new Vector2(0f, this.Scale(2f)), visualPos + visualSize + new Vector2(0f, this.Scale(2f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.7f)), 999f);
        draw.AddRect(visualPos + new Vector2(-this.Scale(1f), -this.Scale(1f)), visualPos + visualSize + new Vector2(this.Scale(1f), this.Scale(1f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.9f)), 999f, ImDrawFlags.None, this.Scale(1f));
        draw.AddRectFilled(visualPos, visualPos + visualSize, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.78f)), 999f);
    }

    private void SyncNotificationWindow()
    {
        if (this.state.Notifications.Count == 0 || !this.CanShowNotifications())
        {
            this.notificationOverlayWindow.IsOpen = false;
            return;
        }

        this.notificationOverlayWindow.IsOpen = true;
    }

    private void PrepareNotificationWindow()
    {
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
    }

    private void DrawNotificationWindowContent()
    {
        if (this.state.Notifications.Count == 0)
        {
            this.notificationOverlayWindow.IsOpen = false;
            return;
        }

        var notification = this.state.Notifications[0];
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
            else if (notification.Tab == PhoneTab.Messages && notification.TargetId is { } conversationId)
            {
                this.OpenConversation(conversationId);
            }

            this.state.Notifications.RemoveAt(0);
        }

        ImGui.SameLine();

        if (ImGui.Button("Dismiss", new Vector2(90f, 26f)))
        {
            this.state.Notifications.RemoveAt(0);
        }
    }

    private void SyncCallWindow()
    {
        if (this.state.ActiveCall is not { } call)
        {
            this.callOverlayWindow.IsOpen = false;
            this.callOverlaySessionId = null;
            return;
        }

        if (this.callOverlaySessionId != call.ConversationId)
        {
            this.callOverlaySessionId = call.ConversationId;
            this.callOverlayWindow.IsOpen = true;
        }
    }

    private void PrepareCallWindow()
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        var height = this.state.ActiveCall is { IsIncoming: true }
            ? this.Scale(300f)
            : this.Scale(250f);
        ImGui.SetNextWindowSize(new Vector2(this.Scale(320f), height), ImGuiCond.Appearing);
    }

    private void DrawCallWindowContent()
    {
        if (this.state.ActiveCall is null)
        {
            this.callOverlayWindow.IsOpen = false;
            this.callOverlaySessionId = null;
            return;
        }

        var call = this.state.ActiveCall;
        ImGui.TextUnformatted(call.IsIncoming ? $"Incoming Call: {call.Title}" : call.Title);
        var elapsed = call.IsIncoming ? "Ringing..." : (DateTimeOffset.UtcNow - call.StartedUtc).ToString(@"hh\:mm\:ss");
        ImGui.TextDisabled(elapsed);
        if (call.VoiceSession is not null)
        {
            ImGui.TextDisabled(call.VoiceSession.QualityLabel);
        }

        if (call.IsIncoming)
        {
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

            var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(10f)) * 0.5f;
            if (ImGui.Button("Accept", new Vector2(actionWidth, this.Scale(34f))))
            {
                this.BeginConversationCall(call.ConversationId, call.IsGroup);
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
            if (ImGui.CollapsingHeader($"Participants ({call.Participants.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                using var participantList = ImRaii.Child("call-popup-participants-active", new Vector2(-1f, this.Scale(96f)), true);
                if (participantList.Success)
                {
                    foreach (var participant in call.Participants.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                    {
                        ImGui.BulletText(participant);
                    }

                    if (call.IsGroup)
                    {
                        ImGui.TextDisabled("Disconnect controls need participant IDs and owner permissions from the server before they can be enabled.");
                    }
                }
            }

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
    }

    private void DrawCopyableText(string text, string copiedValue, string copiedStatus, bool disabled = false)
    {
        using var wrapScope = new ImRaii.TextWrapDisposable().Push(0f);
        if (disabled)
        {
            ImGui.TextDisabled(text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }

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
        using var wrapScope = new ImRaii.TextWrapDisposable().Push(0f);
        ImGui.TextDisabled(text);
    }
    private void DrawNotificationAnchorPicker()
    {
        ImGui.TextDisabled("Notification Spot");
        var anchor = this.configuration.NotificationAnchor;
        using var combo = ImRaii.Combo("##NotificationSpot", anchor.ToString());
        if (combo.Success)
        {
            foreach (NotificationAnchor value in Enum.GetValues(typeof(NotificationAnchor)))
            {
                var selected = value == anchor;
                if (ImGui.Selectable(value.ToString(), selected))
                {
                    this.configuration.NotificationAnchor = value;
                    this.SaveConfiguration();
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
        }
    }

    private void RefreshVoiceDeviceCatalog(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - this.lastVoiceDeviceRefreshUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        this.voiceInputDevices = VoiceAudioDeviceCatalog.GetInputDevices();
        this.voiceOutputDevices = VoiceAudioDeviceCatalog.GetOutputDevices();
        this.lastVoiceDeviceRefreshUtc = now;
    }

    private void DrawVoiceDevicePicker(
        string label,
        string comboId,
        IReadOnlyList<VoiceAudioDeviceInfo> devices,
        VoiceAudioDeviceResolution resolution,
        string? preferredKey,
        string? preferredName,
        Action<VoiceAudioDeviceInfo?> applyPreference)
    {
        ImGui.TextDisabled(label);
        using var combo = ImRaii.Combo(comboId, resolution.DisplayName);
        if (!combo.Success)
        {
            return;
        }

        var defaultSelected = this.IsWindowsDefaultVoiceDeviceSelected(preferredKey, preferredName) || resolution.SavedPreferenceMissing;
        if (ImGui.Selectable("Windows Default", defaultSelected))
        {
            applyPreference(null);
        }

        if (defaultSelected)
        {
            ImGui.SetItemDefaultFocus();
        }

        foreach (var device in devices)
        {
            var selected = !defaultSelected
                && (string.Equals(device.PreferenceKey, preferredKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(device.DisplayName, preferredName, StringComparison.OrdinalIgnoreCase));
            if (ImGui.Selectable(device.DisplayName, selected))
            {
                applyPreference(device);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }
    }

    private void ApplyVoiceInputDevicePreference(VoiceAudioDeviceInfo? device)
    {
        this.ApplyVoiceDevicePreference(device, isInput: true);
    }

    private void ApplyVoiceOutputDevicePreference(VoiceAudioDeviceInfo? device)
    {
        this.ApplyVoiceDevicePreference(device, isInput: false);
    }

    private void ApplyVoiceDevicePreference(VoiceAudioDeviceInfo? device, bool isInput)
    {
        if (isInput)
        {
            this.configuration.PreferredVoiceInputDeviceKey = device?.PreferenceKey;
            this.configuration.PreferredVoiceInputDeviceName = device?.DisplayName;
        }
        else
        {
            this.configuration.PreferredVoiceOutputDeviceKey = device?.PreferenceKey;
            this.configuration.PreferredVoiceOutputDeviceName = device?.DisplayName;
        }

        var label = isInput ? "Voice input" : "Voice output";
        var selectedDevice = device?.DisplayName ?? "Windows default";
        this.pendingStatus = this.state.ActiveCall is null
            ? $"{label} set to {selectedDevice}"
            : $"{label} set to {selectedDevice}. Applies next call.";
        this.SaveConfiguration();
    }

    private bool IsWindowsDefaultVoiceDeviceSelected(string? preferredKey, string? preferredName)
    {
        return string.IsNullOrWhiteSpace(preferredKey) && string.IsNullOrWhiteSpace(preferredName);
    }

    private static string? GetSavedVoiceDeviceMissingMessage(string kind, VoiceAudioDeviceResolution resolution)
    {
        if (!resolution.SavedPreferenceMissing)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(resolution.MissingDeviceName)
            ? $"Saved {kind} device not found, using Windows default."
            : $"Saved {kind} device \"{resolution.MissingDeviceName}\" not found, using Windows default.";
    }

    private static string? GetVoiceDeviceFallbackMessage(VoiceAudioDeviceResolution inputResolution, VoiceAudioDeviceResolution outputResolution)
    {
        if (inputResolution.SavedPreferenceMissing && outputResolution.SavedPreferenceMissing)
        {
            return "Saved voice devices not found, using Windows default.";
        }

        return GetSavedVoiceDeviceMissingMessage("input", inputResolution)
            ?? GetSavedVoiceDeviceMissingMessage("output", outputResolution);
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
        this.lastPhoneWindowCenter = windowPos + (windowSize * 0.5f);
        var shellColor = ImGui.GetColorU32(new Vector4(0.055f, 0.065f, 0.09f, 1f));
        var trimColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
        var screenMin = windowPos + new Vector2(this.Scale(8f), this.Scale(8f));
        var screenMax = windowPos + windowSize - new Vector2(this.Scale(8f), this.Scale(8f));

        drawList.AddRectFilled(windowPos, windowPos + windowSize, shellColor, this.Scale(42f));
        drawList.AddRect(windowPos, windowPos + windowSize, trimColor, this.Scale(42f), ImDrawFlags.None, 1.4f);
        var screenRounding = this.Scale(36f);
        var hasCustomScreenBackground = this.ShouldShowCustomScreenBackground();
        if (hasCustomScreenBackground)
        {
            this.DrawWallpaper(screenMin, screenMax, screenRounding);
        }

        if (!hasCustomScreenBackground)
        {
            drawList.AddRectFilledMultiColor(
                screenMin,
                screenMax,
                ImGui.GetColorU32(new Vector4(0.14f, 0.16f, 0.34f, 0.44f)),
                ImGui.GetColorU32(new Vector4(0.19f, 0.14f, 0.36f, 0.4f)),
                ImGui.GetColorU32(new Vector4(0.03f, 0.08f, 0.18f, 0.52f)),
                ImGui.GetColorU32(new Vector4(0.04f, 0.11f, 0.18f, 0.48f)));
        }
        drawList.AddRect(screenMin, screenMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), screenRounding, ImDrawFlags.None, 1f);
        if (!hasCustomScreenBackground)
        {
            drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.76f, windowSize.Y * 0.2f), windowSize.X * 0.45f, ImGui.GetColorU32(new Vector4(0.98f, 0.72f, 0.42f, 0.08f)), 80);
            drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.18f, windowSize.Y * 0.58f), windowSize.X * 0.34f, ImGui.GetColorU32(new Vector4(0.27f, 0.82f, 0.96f, 0.06f)), 80);
            drawList.AddRectFilled(screenMin + new Vector2(0f, this.Scale(12f)), screenMax - new Vector2(0f, windowSize.Y * 0.68f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.015f)), this.Scale(28f));
        }


    }

    private bool HasCustomScreenBackground()
    {
        return !string.IsNullOrWhiteSpace(this.configuration.BackgroundImagePath)
            || this.configuration.UseSolidBackgroundColor;
    }

    private bool ShouldShowCustomScreenBackground()
    {
        return this.HasCustomScreenBackground()
            && this.startupSplashCompleted
            && (this.showHomeScreen || this.activeTab == PhoneTab.Wallpapers);
    }

    private IDisposable? PushTransparentScreenChildBackgroundIfNeeded()
    {
        return this.ShouldShowCustomScreenBackground()
            ? ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero)
            : null;
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
        var composeHeight = Math.Max(
            this.Scale(268f),
            (ImGui.GetTextLineHeightWithSpacing() * 5.5f) + this.Scale(150f));
        using (var compose = ImRaii.Child("support-compose-card", new Vector2(-1f, composeHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (compose.Success)
            {
                ImGui.TextDisabled("Support");
                ImGui.TextWrapped("Open a support ticket to start a help chat with staff. Your ticket stays readable after staff close it.");
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint($"##support-subject-{this.supportSubjectControlVersion}", "Subject", ref this.supportSubject, 96);
                this.DrawSpellCheckOverlay(SpellFieldSupportSubject, ref this.supportSubject, () => this.supportSubjectControlVersion++);
                ImGui.TextDisabled("What do you need help with?");
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextMultiline(
                    $"##support-body-{this.supportBodyControlVersion}",
                    ref this.supportBody,
                    512,
                    new Vector2(-1f, this.Scale(86f)),
                    ImGuiInputTextFlags.None);
                this.DrawSpellCheckOverlay(SpellFieldSupportBody, ref this.supportBody, () => this.supportBodyControlVersion++);
                if (this.DrawPhonePillButton("Open Support Ticket", new Vector2(-1f, this.Scale(34f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var authToken = this.configuration.AuthToken;
                    var request = new CreateSupportTicketRequest(this.supportSubject, this.supportBody, false);
                    this.QueueUiOperation("support-ticket-create", () => this.client.CreateSupportTicketAsync(authToken, request), ticket =>
                    {
                        this.UpsertSupportTicket(ticket);
                        this.supportSubject = string.Empty;
                        this.supportBody = string.Empty;
                        this.supportSubjectControlVersion++;
                        this.supportBodyControlVersion++;
                        this.RefreshSnapshot();
                        this.OpenConversation(ticket.ConversationId, PhoneTab.Support);
                        this.pendingStatus = "Support ticket opened";
                    }, "Opening support ticket...");
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(10f)));
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

            var orderedTickets = this.state.SupportTickets
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToList();

            for (var index = 0; index < orderedTickets.Count; index++)
            {
                var ticket = orderedTickets[index];
                ImGui.TextUnformatted(ticket.Subject);
                var ticketTimestamp = ticket.Status == SupportTicketStatus.Closed && ticket.ClosedAtUtc is not null
                    ? $"Closed {ticket.ClosedAtUtc.Value.LocalDateTime:g}"
                    : $"{ticket.Status}  {ticket.CreatedAtUtc.LocalDateTime:g}";
                ImGui.TextDisabled(ticketTimestamp);
                if (!string.IsNullOrWhiteSpace(ticket.Body))
                {
                    ImGui.TextWrapped(ticket.Body);
                }

                if (this.DrawPhonePillButton($"Open Ticket##support-open-{ticket.Id}", new Vector2(this.Scale(138f), this.Scale(32f))))
                {
                    this.OpenConversation(ticket.ConversationId, PhoneTab.Support);
                }

                this.DrawStaffListSeparator(index, orderedTickets.Count);
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

        if (this.refreshStaffDashboardOnOpen)
        {
            this.refreshStaffDashboardOnOpen = false;
            try
            {
                this.RefreshStaffDashboard();
            }
            catch (Exception ex)
            {
                this.pendingStatus = $"Staff refresh failed: {this.SanitizeUserFacingError(ex.Message)}";
            }
        }

        var dashboard = this.adminDashboard;
        var topHeight = this.Scale(dashboard is null ? 132f : 160f);
        using (var summary = ImRaii.Child("staff-summary-card", new Vector2(-1f, topHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (summary.Success)
            {
                ImGui.TextDisabled("Staff Console");
                ImGui.TextWrapped("Use the tabs below to work staff chat, tickets, accounts, reports, and logs from one place.");
                ImGui.Spacing();

                var refreshWidth = Math.Max(this.Scale(110f), ImGui.CalcTextSize("Refresh").X + this.Scale(28f));
                if (this.DrawPhonePillButton("Refresh", new Vector2(refreshWidth, this.Scale(34f))))
                {
                    this.RefreshStaffDashboard();
                    this.RefreshSnapshot();
                }

                if (dashboard is not null)
                {
                    ImGui.Spacing();
                    ImGui.TextDisabled($"Online now: {dashboard.Accounts.Count(account => account.IsOnline)} of {dashboard.Accounts.Count}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"Open tickets: {dashboard.Tickets.Count(ticket => ticket.Status == SupportTicketStatus.Open)}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"Open reports: {dashboard.Reports.Count(report => report.Status == ReportStatus.Open)}");
                }
            }
        }

        if (dashboard is null)
        {
            ImGui.TextDisabled("Refresh the staff console to load staff data.");
            return;
        }

        using var staffTabs = ImRaii.TabBar("staff-console-tabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (staffTabs.Success)
        {
            using (var chatTab = ImRaii.TabItem("Chat"))
            {
                if (chatTab.Success)
                {
                    this.DrawStaffChatTab(dashboard);
                }
            }

            using (var ticketsTab = ImRaii.TabItem("Tickets"))
            {
                if (ticketsTab.Success)
                {
                    this.DrawStaffTicketsTab(dashboard);
                }
            }

            using (var accountsTab = ImRaii.TabItem("Accounts"))
            {
                if (accountsTab.Success)
                {
                    this.DrawStaffAccountsTab(dashboard);
                }
            }

            using (var reportsTab = ImRaii.TabItem("Reports"))
            {
                if (reportsTab.Success)
                {
                    this.DrawStaffReportsTab(dashboard);
                }
            }

            using (var auditTab = ImRaii.TabItem("Audit"))
            {
                if (auditTab.Success)
                {
                    this.DrawStaffAuditTab(dashboard);
                }
            }

            if (this.state.CurrentProfile.Role == AccountRole.Owner)
            {
                using var ownerTab = ImRaii.TabItem("Owner");
                if (ownerTab.Success)
                {
                    this.DrawStaffOwnerTab();
                }
            }
        }
    }

    private void DrawStaffChatTab(AdminDashboardSnapshot dashboard)
    {
        using var scroll = ImRaii.Child("staff-chat-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        ImGui.TextDisabled("Staff Chat");
        ImGui.TextWrapped("Use the shared staff room for live coordination and internal support handoff.");
        ImGui.Spacing();
        var openStaffChatWidth = Math.Max(this.Scale(164f), ImGui.CalcTextSize("Open Staff Chat").X + this.Scale(30f));
        if (this.DrawPhonePillButton("Open Staff Chat", new Vector2(openStaffChatWidth, this.Scale(34f))))
        {
            this.OpenStaffConversation();
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"Online now: {dashboard.Accounts.Count(account => account.IsOnline)} of {dashboard.Accounts.Count}");
        ImGui.TextDisabled($"Open tickets: {dashboard.Tickets.Count(ticket => ticket.Status == SupportTicketStatus.Open)}");
        ImGui.TextDisabled($"Open reports: {dashboard.Reports.Count(report => report.Status == ReportStatus.Open)}");

        if (dashboard.ActiveAnnouncement is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextDisabled("Active Announcement");
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(dashboard.ActiveAnnouncement.Title) ? "Server Notice" : dashboard.ActiveAnnouncement.Title);
            ImGui.TextDisabled($"{dashboard.ActiveAnnouncement.CreatedByDisplayName}  {dashboard.ActiveAnnouncement.CreatedAtUtc.LocalDateTime:g}");
            if (!string.IsNullOrWhiteSpace(dashboard.ActiveAnnouncement.Body))
            {
                ImGui.TextWrapped(dashboard.ActiveAnnouncement.Body);
            }
        }
    }

    private void DrawStaffTicketsTab(AdminDashboardSnapshot dashboard)
    {
        using var scroll = ImRaii.Child("staff-tickets-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        var tickets = dashboard.Tickets.OrderByDescending(item => item.CreatedAtUtc).ToList();
        ImGui.TextDisabled("Support Tickets");
        if (tickets.Count == 0)
        {
            ImGui.TextDisabled("No support tickets.");
            return;
        }

        for (var index = 0; index < tickets.Count; index++)
        {
            var ticket = tickets[index];
            ImGui.TextUnformatted(ticket.Subject);
            var ticketTimestamp = ticket.Status == SupportTicketStatus.Closed && ticket.ClosedAtUtc is not null
                ? $"Closed {ticket.ClosedAtUtc.Value.LocalDateTime:g}"
                : $"Opened {ticket.CreatedAtUtc.LocalDateTime:g}";
            this.DrawWrappedDisabledText($"{ticket.OwnerDisplayName}  {ticket.Status}  {ticketTimestamp}");
            if (!string.IsNullOrWhiteSpace(ticket.Body))
            {
                ImGui.TextWrapped(ticket.Body);
            }

            var actionSpacing = this.Scale(8f);
            var openChatWidth = Math.Max(this.Scale(124f), ImGui.CalcTextSize("Open Chat").X + this.Scale(28f));
            if (this.DrawPhonePillButton($"Open Chat##staff-open-ticket-{ticket.Id}", new Vector2(openChatWidth, this.Scale(32f))))
            {
                this.OpenConversation(ticket.ConversationId, PhoneTab.Staff);
            }

            if (ticket.Status == SupportTicketStatus.Open)
            {
                ImGui.SameLine(0f, actionSpacing);
                var addButtonWidth = Math.Max(this.Scale(68f), ImGui.CalcTextSize("Add").X + this.Scale(28f));
                var closeWidth = Math.Max(this.Scale(84f), ImGui.CalcTextSize("Close").X + this.Scale(28f));
                var inputWidth = Math.Max(this.Scale(124f), ImGui.GetContentRegionAvail().X - closeWidth - addButtonWidth - actionSpacing * 2f);
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputTextWithHint($"##staff-ticket-add-{ticket.Id}", "Add participant", ref this.staffTicketParticipantTarget, 64);
                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton($"Add##staff-ticket-add-btn-{ticket.Id}", new Vector2(addButtonWidth, this.Scale(32f))))
                {
                    this.AddSupportTicketParticipant(ticket.Id, this.staffTicketParticipantTarget, false);
                }

                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton($"Close##staff-ticket-close-{ticket.Id}", new Vector2(closeWidth, this.Scale(32f))))
                {
                    this.CloseSupportTicket(ticket.Id, false);
                }
            }

            this.DrawStaffListSeparator(index, tickets.Count);
        }
    }

    private void DrawStaffAccountsTab(AdminDashboardSnapshot dashboard)
    {
        using var scroll = ImRaii.Child("staff-accounts-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        ImGui.TextDisabled("Accounts");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##staff-search", "Search username, display, or phone", ref this.staffSearchQuery, 64);
        ImGui.Spacing();

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
            return;
        }

        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            ImGui.TextUnformatted($"{account.DisplayName} (@{account.Username})");
            var onlineLabel = account.IsOnline ? "Online now" : $"Last seen {account.LastSeenAtUtc?.LocalDateTime:g}";
            this.DrawWrappedDisabledText($"{account.Role}  {account.Status}  {account.PhoneNumber}  {onlineLabel}");
            if (account.KnownIpAddresses.Count > 0)
            {
                this.DrawWrappedDisabledText($"IPs: {string.Join(", ", account.KnownIpAddresses)}");
            }

            if (this.state.CurrentProfile.Role == AccountRole.Owner && account.Role != AccountRole.Owner && account.AccountId != this.state.CurrentProfile.AccountId)
            {
                var actionSpacing = this.Scale(8f);
                var roleWidth = (ImGui.GetContentRegionAvail().X - actionSpacing * 2f) / 3f;
                if (this.DrawPhonePillButton($"User##role-user-{account.AccountId}", new Vector2(roleWidth, this.Scale(32f))))
                {
                    this.UpdateAccountRole(account, AccountRole.User);
                }
                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton($"Moderator##role-mod-{account.AccountId}", new Vector2(roleWidth, this.Scale(32f))))
                {
                    this.UpdateAccountRole(account, AccountRole.Moderator);
                }
                ImGui.SameLine(0f, actionSpacing);
                if (this.DrawPhonePillButton($"Admin##role-admin-{account.AccountId}", new Vector2(roleWidth, this.Scale(32f))))
                {
                    this.UpdateAccountRole(account, AccountRole.Admin);
                }
            }

            this.DrawStaffListSeparator(index, accounts.Count);
        }
    }

    private void UpdateAccountRole(AdminAccountSummary account, AccountRole role)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        var authToken = this.configuration.AuthToken;
        this.QueueUiOperation($"account-role-{account.AccountId}",
            () => this.client.UpdateAccountRoleAsync(authToken, new UpdateAccountRoleRequest(account.AccountId, role)),
            _ =>
            {
                this.RefreshSnapshot();
                this.RefreshStaffDashboard();
                this.pendingStatus = $"{account.Username} is now {role}";
            },
            $"Updating {account.Username}...");
    }

    private void DrawStaffReportsTab(AdminDashboardSnapshot dashboard)
    {
        using var scroll = ImRaii.Child("staff-reports-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        var reports = dashboard.Reports.OrderByDescending(item => item.CreatedAtUtc).ToList();
        ImGui.TextDisabled("Reports");
        if (reports.Count == 0)
        {
            ImGui.TextDisabled("No open reports.");
            return;
        }

        for (var index = 0; index < reports.Count; index++)
        {
            var report = reports[index];
            ImGui.TextUnformatted($"{report.Category} [{report.Status}]");
            var meta = $"{report.ReporterDisplayName}  {report.CreatedAtUtc.LocalDateTime:g}";
            if (report.SuspectedCsam)
            {
                meta += "  Flagged";
            }

            this.DrawWrappedDisabledText(meta);
            if (!string.IsNullOrWhiteSpace(report.Reason))
            {
                ImGui.TextWrapped(report.Reason);
            }

            this.DrawStaffListSeparator(index, reports.Count);
        }
    }

    private void DrawStaffAuditTab(AdminDashboardSnapshot dashboard)
    {
        using var scroll = ImRaii.Child("staff-audit-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        var logs = dashboard.AuditLogs.OrderByDescending(item => item.CreatedAtUtc).ToList();
        ImGui.TextDisabled("Audit Logs");
        if (logs.Count == 0)
        {
            ImGui.TextDisabled("No audit logs.");
            return;
        }

        for (var index = 0; index < logs.Count; index++)
        {
            var log = logs[index];
            ImGui.TextUnformatted($"{log.EventType}  {log.CreatedAtUtc.LocalDateTime:g}");
            if (!string.IsNullOrWhiteSpace(log.ActorDisplayName))
            {
                ImGui.TextDisabled(log.ActorDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(log.Summary))
            {
                ImGui.TextWrapped(log.Summary);
            }

            this.DrawStaffListSeparator(index, logs.Count);
        }
    }

    private void DrawStaffOwnerTab()
    {
        using var scroll = ImRaii.Child("staff-owner-tab-scroll", new Vector2(-1f, 0f), true);
        if (!scroll.Success)
        {
            return;
        }

        ImGui.TextDisabled("Owner Password Reset");
        ImGui.TextWrapped("Reset an account password directly by account id. Use this carefully.");
        ImGui.Spacing();
        ImGui.TextDisabled("Target Account Id");
        ImGui.InputText("##owner-reset-target", ref this.ownerResetTarget, 64);
        ImGui.TextDisabled("New Owner Password");
        ImGui.InputText("##owner-reset-password", ref this.ownerResetPassword, 64, ImGuiInputTextFlags.Password);
        var resetOwnerPasswordWidth = Math.Max(this.Scale(212f), ImGui.CalcTextSize("Reset Account Password").X + this.Scale(30f));
        if (this.DrawPhonePillButton("Reset Account Password", new Vector2(resetOwnerPasswordWidth, this.Scale(34f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && Guid.TryParse(this.ownerResetTarget, out var targetAccountId))
        {
            var authToken = this.configuration.AuthToken;
            var request = new AdminPasswordResetRequest(targetAccountId, this.ownerResetPassword);
            this.QueueUiOperation($"owner-password-reset-{targetAccountId}", () => this.client.ResetPasswordAsOwnerAsync(authToken, request), success =>
            {
                this.pendingStatus = success ? "Owner reset complete" : "Owner reset failed";
                if (success)
                {
                    this.ownerResetPassword = string.Empty;
                }
            }, "Resetting account password...");
        }
    }

    private void DrawStaffListSeparator(int index, int totalCount)
    {
        if (index >= totalCount - 1)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, this.Scale(6f)));
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

        using var legalPopup = ImRaii.PopupModal("TomestonePhone Legal Terms", ImGuiWindowFlags.NoResize);
        if (legalPopup.Success)
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
            using var declineDisabled = new ImRaii.DisabledDisposable().Push();
            ImGui.Button("Decline", new Vector2(100f, 28f));
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

        using var privacyPopup = ImRaii.PopupModal("TomestonePhone Privacy Policy", ImGuiWindowFlags.NoResize);
        if (privacyPopup.Success)
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

        using var openingEmotePopup = ImRaii.PopupModal("TomestonePhone Opening Emote", ImGuiWindowFlags.NoResize);
        if (openingEmotePopup.Success)
        {
            ImGui.TextWrapped("Would you like TomestonePhone to run /tomestone when you open the app with /ts?");
            ImGui.Spacing();
            ImGui.TextWrapped("This only plays on open, never on close, and you can change it later in Settings.");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.96f, 0.74f, 0.33f, 1f), "Warning: This is an automation and is often frowned upon. Use at your own risk.");
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

    private Vector2 GetPhoneWindowCenter()
    {
        if (this.lastPhoneWindowCenter != default)
        {
            return this.lastPhoneWindowCenter;
        }

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        return windowPos + (windowSize * 0.5f);
    }

    private void PreparePhoneModal(Vector2 size)
    {
        ImGui.SetNextWindowPos(this.GetPhoneWindowCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
    }

    private void PreparePhoneModal(Vector2 minimumSize, Vector2 maximumSize)
    {
        ImGui.SetNextWindowPos(this.GetPhoneWindowCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(minimumSize, maximumSize);
    }

    private void PrepareConfirmModal(string text, string confirmLabel, Vector2 confirmButtonSize)
    {
        var style = ImGui.GetStyle();
        var cancelButtonSize = this.Scale(110f, 32f);
        var confirmButtonWidth = Math.Max(confirmButtonSize.X, ImGui.CalcTextSize(confirmLabel).X + this.Scale(28f));
        var actionRowWidth = cancelButtonSize.X + style.ItemSpacing.X + confirmButtonWidth;
        var contentWidth = Math.Clamp(
            Math.Max(actionRowWidth, this.Scale(260f)),
            this.Scale(260f),
            this.Scale(340f));
        var textHeight = ImGui.CalcTextSize(text, false, contentWidth).Y;
        var titleHeight = ImGui.GetTextLineHeight() + (this.Scale(3f) * 2f);
        var height = titleHeight + (style.WindowPadding.Y * 2f) + textHeight + style.ItemSpacing.Y + confirmButtonSize.Y + this.Scale(8f);
        var width = contentWidth + (style.WindowPadding.X * 2f);
        var size = new Vector2(width, Math.Clamp(height, this.Scale(142f), this.Scale(320f)));
        this.PreparePhoneModal(size);
    }

    private void DrawConfirmModalText(string text)
    {
        using var wrap = new ImRaii.TextWrapDisposable().Push(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(text);
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

        using var externalLinkPopup = ImRaii.PopupModal("TomestonePhone External Link", ImGuiWindowFlags.NoResize);
        if (externalLinkPopup.Success)
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
        using var bubbleWrap = new ImRaii.TextWrapDisposable().Push(bubbleMin.X + bubblePadding.X + bubbleInnerWidth);
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
        if (!this.configuration.ShareGameIdentity)
        {
            return null;
        }

        var player = this.service.ObjectTable.LocalPlayer;
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


    private async Task<IReadOnlyList<Guid>> ResolveConversationTargetsAsync(string authToken, string rawTargets)
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return [];
        }

        var accountIds = new HashSet<Guid>();
        var targets = rawTargets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var target in targets)
        {
            var matches = await this.client.SearchPeopleAsync(authToken, target).ConfigureAwait(false);
            var match = matches.FirstOrDefault(item => string.Equals(item.PhoneNumber, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Username, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.DisplayName, target, StringComparison.OrdinalIgnoreCase))
                ?? (matches.Count == 1 ? matches[0] : null);
            if (match is not null)
            {
                accountIds.Add(match.AccountId);
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

        if (this.pendingAuthTask is { IsCompleted: false }
            || this.pendingSnapshotTask is { IsCompleted: false }
            || this.pendingConversationMessagesTask is { IsCompleted: false }
            || this.pendingConversationDetailTask is { IsCompleted: false })
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
            this.pendingConversationDetailTask = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversationId);
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
        foreach (var operation in this.pendingUiOperations.Where(item => item.Task.IsCompleted).ToList())
        {
            this.pendingUiOperations.Remove(operation);
            var completion = operation.Task.GetAwaiter().GetResult();
            if (operation.Generation != this.uiOperationGeneration)
            {
                continue;
            }

            if (completion.Error is not null)
            {
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(completion.Error.Message) ? "Request failed" : completion.Error.Message);
                this.AnnounceDebugOnce($"{operation.Key} failed: {this.pendingStatus}", completion.Error);
                try
                {
                    operation.OnError?.Invoke(completion.Error);
                }
                catch (Exception callbackError)
                {
                    this.service.Log.Error(callbackError, $"Failure callback for {operation.Key} threw an exception.");
                }
                continue;
            }

            try
            {
                completion.Apply?.Invoke();
            }
            catch (Exception callbackError)
            {
                this.pendingStatus = "The request completed, but the screen could not update";
                this.service.Log.Error(callbackError, $"Success callback for {operation.Key} threw an exception.");
            }
        }

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

        if (this.pendingConversationDetailTask is { IsCompleted: true })
        {
            try
            {
                var detail = this.pendingConversationDetailTask.GetAwaiter().GetResult();
                this.pendingConversationDetailTask = null;
                if (this.selectedConversationId == detail.Id)
                {
                    this.selectedConversationDetail = detail;
                }
            }
            catch (Exception ex)
            {
                this.pendingConversationDetailTask = null;
                this.pendingStatus = this.SanitizeUserFacingError(string.IsNullOrWhiteSpace(ex.Message) ? "Conversation refresh failed" : ex.Message);
                this.AnnounceDebugOnce($"Conversation refresh failed: {this.pendingStatus}", ex);
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
                this.ProcessFriendNotifications(result.Snapshot);
                this.ProcessConversationNotifications(result.Snapshot);
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

    private void QueueUiOperation<T>(string key, Func<Task<T>> operation, Action<T> onSuccess, string? statusMessage, Action<Exception>? onError = null)
    {
        if (this.pendingUiOperations.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal)))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            this.pendingStatus = statusMessage;
        }
        this.pendingUiOperations.Add(new PendingUiOperation(key, this.uiOperationGeneration, CompleteUiOperationAsync(operation, onSuccess), onError));
    }

    private static async Task<UiOperationCompletion> CompleteUiOperationAsync<T>(Func<Task<T>> operation, Action<T> onSuccess)
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            return new UiOperationCompletion(() => onSuccess(result), null);
        }
        catch (Exception ex)
        {
            return new UiOperationCompletion(null, ex);
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
        this.InvalidateUiOperations();
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
        this.knownIncomingFriendRequestIds.Clear();
        this.state.Notifications = seeded.Notifications;
        this.state.VisibleReports = seeded.VisibleReports;
        this.state.VisibleAuditLogs = seeded.VisibleAuditLogs;
        this.state.SupportTickets = seeded.SupportTickets;
        this.state.ActiveCall = null;
        this.activeCallSessions = [];
        this.seenIncomingDirectCallSessionIds.Clear();
        this.ClearSelectedConversation();
        this.showHomeScreen = false;
        this.activeTab = PhoneTab.Settings;
        this.pendingAuthTask = null;
        this.pendingSnapshotTask = null;
        this.pendingActiveCallsTask = null;
        this.lastHeartbeatUtc = DateTimeOffset.MinValue;
        this.refreshOnNextDraw = false;
        this.pendingStatus = statusMessage;
        this.autoLoginAttempted = resetAutoLoginAttempted ? false : this.autoLoginAttempted;
        if (!clearStoredUsername && !string.IsNullOrWhiteSpace(this.configuration.Username))
        {
            this.loginUsername = this.configuration.Username;
        }
        this.SaveConfiguration();
    }

    private void InvalidateUiOperations()
    {
        this.uiOperationGeneration++;
        this.pendingUiOperations.Clear();
    }

    private void ProcessFriendNotifications(PhoneSnapshot snapshot)
    {
        var notificationAccountChanged = this.configuration.FriendNotificationAccountId != snapshot.Profile.AccountId;
        if (notificationAccountChanged)
        {
            this.configuration.FriendNotificationAccountId = snapshot.Profile.AccountId;
            this.configuration.SeenIncomingFriendRequestIds.Clear();
            this.configuration.PendingOutgoingFriendRequestNotices.Clear();
        }

        var incomingRequests = snapshot.FriendRequests
            .Where(item => item.Status == FriendRequestStatus.Pending && item.IsIncoming)
            .ToList();

        var friendsAppOpen = this.IsOpen && !this.showHomeScreen && this.activeTab == PhoneTab.Friends;
        var notificationsAllowed = !friendsAppOpen
            && !snapshot.Profile.NotificationsMuted
            && snapshot.Profile.PresenceStatus != PhonePresenceStatus.DoNotDisturb;
        var seenIncoming = this.configuration.SeenIncomingFriendRequestIds.ToHashSet();
        var configurationChanged = notificationAccountChanged || !seenIncoming.SetEquals(incomingRequests.Select(item => item.Id));
        if (notificationsAllowed)
        {
            foreach (var request in incomingRequests.Where(item => !seenIncoming.Contains(item.Id)))
            {
                this.state.Notifications.Add(new PhoneNotification(Guid.NewGuid(), "Friend Request", $"{request.DisplayName} sent you a friend request", PhoneTab.Friends, null, false));
            }
        }

        var currentOutgoing = snapshot.FriendRequests
            .Where(item => item.Status == FriendRequestStatus.Pending && !item.IsIncoming)
            .ToList();
        foreach (var request in currentOutgoing)
        {
            if (this.configuration.PendingOutgoingFriendRequestNotices.All(item => item.RequestId != request.Id))
            {
                this.configuration.PendingOutgoingFriendRequestNotices.Add(new PendingFriendRequestNotice
                {
                    RequestId = request.Id,
                    DisplayName = request.DisplayName,
                    PhoneNumber = request.PhoneNumber,
                });
                configurationChanged = true;
            }
        }

        var currentOutgoingIds = currentOutgoing.Select(item => item.Id).ToHashSet();
        foreach (var request in this.configuration.PendingOutgoingFriendRequestNotices.Where(item => !currentOutgoingIds.Contains(item.RequestId)).ToList())
        {
            var accepted = snapshot.Friends.Any(friend => string.Equals(friend.FriendPhoneNumber, request.PhoneNumber, StringComparison.OrdinalIgnoreCase));
            if (accepted && notificationsAllowed)
            {
                this.state.Notifications.Add(new PhoneNotification(Guid.NewGuid(), "Friend Request Accepted", $"{request.DisplayName} accepted your friend request", PhoneTab.Friends, null, false));
            }

            this.configuration.PendingOutgoingFriendRequestNotices.Remove(request);
            configurationChanged = true;
        }

        this.knownIncomingFriendRequestIds = incomingRequests.Select(item => item.Id).ToHashSet();
        this.configuration.SeenIncomingFriendRequestIds = this.knownIncomingFriendRequestIds.ToList();
        if (configurationChanged)
        {
            this.SaveConfiguration();
        }
    }

    private bool CanShowNotifications()
    {
        return !this.state.CurrentProfile.NotificationsMuted
            && this.state.CurrentProfile.PresenceStatus != PhonePresenceStatus.DoNotDisturb;
    }

    private void ProcessConversationNotifications(PhoneSnapshot snapshot)
    {
        var accountChanged = this.configuration.ConversationNotificationAccountId != snapshot.Profile.AccountId;
        if (accountChanged)
        {
            this.configuration.ConversationNotificationAccountId = snapshot.Profile.AccountId;
            this.configuration.KnownConversationActivityUtc.Clear();
        }

        var known = this.configuration.KnownConversationActivityUtc;
        var currentIds = snapshot.Conversations.Select(item => item.Id).ToHashSet();
        var changed = accountChanged;
        var notificationsAllowed = !snapshot.Profile.NotificationsMuted
            && snapshot.Profile.PresenceStatus != PhonePresenceStatus.DoNotDisturb;

        foreach (var conversation in snapshot.Conversations)
        {
            var hadPrevious = known.TryGetValue(conversation.Id, out var previousActivity);
            var hasNewActivity = conversation.LastActivityUtc > DateTimeOffset.MinValue
                && ((!accountChanged && !hadPrevious) || (hadPrevious && conversation.LastActivityUtc > previousActivity));
            var conversationOpen = this.IsOpen
                && !this.showHomeScreen
                && this.activeTab == PhoneTab.Messages
                && this.selectedConversationId == conversation.Id;
            if (hasNewActivity && notificationsAllowed && !conversationOpen)
            {
                var body = string.IsNullOrWhiteSpace(conversation.LastMessagePreview)
                    ? $"New activity in {conversation.DisplayName}"
                    : conversation.LastMessagePreview;
                this.state.Notifications.Add(new PhoneNotification(Guid.NewGuid(), conversation.DisplayName, body, PhoneTab.Messages, conversation.Id, false));
            }

            if (!hadPrevious || previousActivity != conversation.LastActivityUtc)
            {
                known[conversation.Id] = conversation.LastActivityUtc;
                changed = true;
            }
        }

        foreach (var removedId in known.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            known.Remove(removedId);
            changed = true;
        }

        if (changed)
        {
            this.SaveConfiguration();
        }
    }

    private void RecordConversationActivity(Guid conversationId, DateTimeOffset activityUtc)
    {
        if (this.state.CurrentProfile.AccountId == Guid.Empty)
        {
            return;
        }

        this.configuration.ConversationNotificationAccountId = this.state.CurrentProfile.AccountId;
        this.configuration.KnownConversationActivityUtc[conversationId] = activityUtc;
        this.SaveConfiguration();
    }

    private async Task<PostAuthSnapshotResult> LoadPostAuthSnapshotAsync(string authToken, GameIdentityRecord? identity)
    {
        try
        {
            var snapshot = await this.client.GetSnapshotAsync(authToken).ConfigureAwait(false);
            PhoneProfile? profile = null;
            if (identity is not null || snapshot.Profile.LastKnownGameIdentity is not null)
            {
                try
                {
                    var request = identity is null
                        ? new UpdateGameIdentityRequest(string.Empty, string.Empty)
                        : new UpdateGameIdentityRequest(identity.CharacterName, identity.WorldName);
                    profile = await this.client.UpdateGameIdentityAsync(authToken, request).ConfigureAwait(false);
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

    private sealed class SpellFieldState
    {
        public string Text { get; set; } = string.Empty;

        public SpellCheckAnalysis Analysis { get; set; } = SpellCheckAnalysis.Empty;
    }

    private sealed class GroupMembersOverlayWindow : Window
    {
        private readonly PhoneWindow parent;

        public GroupMembersOverlayWindow(PhoneWindow parent)
            : base("Members###TomestonePhoneGroupMembers")
        {
            this.parent = parent;
            this.Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize;
            this.IsOpen = false;
            this.RespectCloseHotkey = false;
        }

        public Guid ConversationId { get; set; }

        public override void PreDraw()
        {
            ImGui.SetNextWindowPos(this.parent.GetPhoneWindowCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(this.parent.Scale(480f, 540f), ImGuiCond.Appearing);
        }

        public override void Draw()
        {
            this.parent.DrawGroupMembersWindowContent(this.ConversationId);
        }

        public override void OnClose()
        {
            this.parent.showGroupMembersWindow = false;
        }
    }

    private sealed class NotificationOverlayWindow : Window
    {
        private readonly PhoneWindow parent;

        public NotificationOverlayWindow(PhoneWindow parent)
            : base("TomestonePhoneNotification")
        {
            this.parent = parent;
            this.Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove;
            this.IsOpen = false;
            this.RespectCloseHotkey = false;
        }

        public override void PreDraw()
        {
            this.parent.PrepareNotificationWindow();
        }

        public override void Draw()
        {
            this.parent.DrawNotificationWindowContent();
        }
    }

    private sealed class CallOverlayWindow : Window
    {
        private readonly PhoneWindow parent;

        public CallOverlayWindow(PhoneWindow parent)
            : base("Call###TomestonePhoneCallPopup")
        {
            this.parent = parent;
            this.Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
            this.IsOpen = false;
            this.RespectCloseHotkey = false;
        }

        public override void PreDraw()
        {
            this.parent.PrepareCallWindow();
        }

        public override void Draw()
        {
            this.parent.DrawCallWindowContent();
        }

        public override void OnClose()
        {
            this.parent.callOverlaySessionId = null;
            this.parent.LeaveCurrentCall();
        }
    }

    private sealed record AuthResult(string? Username, string? AuthToken, string? StatusMessage, Exception? Error);

    private sealed record PostAuthSnapshotResult(PhoneSnapshot? Snapshot, PhoneProfile? UpdatedProfile, Exception? Error);

    private sealed record PendingUiOperation(string Key, long Generation, Task<UiOperationCompletion> Task, Action<Exception>? OnError);

    private sealed record UiOperationCompletion(Action? Apply, Exception? Error);
}
