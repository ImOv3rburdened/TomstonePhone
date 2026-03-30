using System.Numerics;
using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using TomestonePhone.Networking;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.UI;

public sealed class PhoneWindow : Window
{
    private const float PhoneAspectRatio = 390f / 844f;
    private const float DefaultWindowWidth = 440f;
    private const float DefaultWindowHeight = 952f;
    private const string GiphyCreateAppUrl = "https://developers.giphy.com/dashboard/?create=true";
    private readonly Service service;
    private readonly Configuration configuration;
    private readonly PhoneState state;
    private readonly TomestonePhoneClient client;
    private readonly GifEmbedRenderer gifEmbedRenderer;
    private readonly GiphyClient giphyClient = new();
    private PhoneTab activeTab = PhoneTab.Messages;
    private bool showHomeScreen = true;
    private string loginUsername = string.Empty;
    private string loginPassword = string.Empty;
    private string pendingStatus = "Disconnected";
    private Vector2 lastWindowSize = new(390f, 844f);
    private bool localTermsCheckbox;
    private bool localPrivacyCheckbox;
    private string supportSubject = string.Empty;
    private string supportBody = string.Empty;
    private string oldPassword = string.Empty;
    private string newPassword = string.Empty;
    private string confirmPassword = string.Empty;
    private string ownerResetTarget = string.Empty;
    private string ownerResetPassword = string.Empty;
    private AdminDashboardSnapshot? adminDashboard;
    private Guid? selectedConversationId;
    private ConversationMessagePage? selectedConversationMessages;
    private ConversationDetail? selectedConversationDetail;
    private string composeMessage = string.Empty;
    private string composeEmbedUrl = string.Empty;
    private string directMessageTarget = string.Empty;
    private string groupAddTarget = string.Empty;
    private string groupCreateName = string.Empty;
    private string groupCreateTargets = string.Empty;
    private string contactAddTarget = string.Empty;
    private string callTarget = string.Empty;
    private string friendRequestTarget = string.Empty;
    private string friendRequestMessage = string.Empty;
    private string reportReplyBody = string.Empty;
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
    private bool refreshOnNextDraw = true;
    private bool autoLoginAttempted;
    private string? lastChatDebugMessage;

    public PhoneWindow(Service service, Configuration configuration, PhoneState state, TomestonePhoneClient client)
        : base("TomestonePhone###TomestonePhoneMain")
    {
        this.service = service;
        this.configuration = configuration;
        this.state = state;
        this.client = client;
        this.gifEmbedRenderer = new GifEmbedRenderer(service.TextureProvider);
        this.Flags = ImGuiWindowFlags.NoCollapse;
        this.Size = new Vector2(DefaultWindowWidth, DefaultWindowHeight);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 909f),
            MaximumSize = new Vector2(720, 1558f),
        };
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
        this.autoLoginAttempted = false;
    }

    public void DisposeResources()
    {
        this.gifEmbedRenderer.Dispose();
    }

    private float GetUiScale()
    {
        var size = ImGui.GetWindowSize();
        var widthScale = size.X <= 0f ? 1f : size.X / DefaultWindowWidth;
        var heightScale = size.Y <= 0f ? 1f : size.Y / DefaultWindowHeight;
        return Math.Clamp(Math.Min(widthScale, heightScale), 0.9f, 1f);
    }

    private float Scale(float value)
    {
        return value * this.GetUiScale();
    }

    private Vector2 Scale(float x, float y)
    {
        return new Vector2(this.Scale(x), this.Scale(y));
    }

    public override void Draw()
    {
        this.ProcessBackgroundTasks();
        this.TryBeginAutoLogin();
        this.EnsureSessionHydrated();
        this.EnforceAspectRatio();

        var uiScale = this.GetUiScale();
        ImGui.SetWindowFontScale(uiScale);
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(12f * uiScale, 8f * uiScale));
        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(10f * uiScale, 10f * uiScale));
        using var theme = PhoneTheme.Push(this.configuration);
        this.DrawPhoneShell();
        this.DrawNotifications();
        this.DrawLegalModal();
        this.DrawPrivacyModal();
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

            if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
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
                }
            }
        }

        using var homeBar = ImRaii.Child("TomestonePhoneHomeBar", new Vector2(-1f, footerHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (homeBar.Success)
        {
            this.DrawHomeButton();
        }
    }
    private void DrawHeader()
    {
        var topStart = ImGui.GetCursorScreenPos();
        var topWidth = ImGui.GetContentRegionAvail().X;
        var topHeight = this.Scale(64f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(22f));
        draw.AddRect(topStart, topStart + new Vector2(topWidth, topHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(22f));

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(10f)));
        ImGui.TextDisabled(DateTime.Now.ToString("h:mm"));
        var rightLabel = "5G   |||   88%";
        var rightSize = ImGui.CalcTextSize(rightLabel);
        ImGui.SameLine(topWidth - rightSize.X - this.Scale(18f));
        ImGui.TextDisabled(rightLabel);

        ImGui.SetCursorScreenPos(topStart + new Vector2(this.Scale(14f), this.Scale(31f)));
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
        ImGui.Dummy(new Vector2(topWidth, topHeight + this.Scale(6f)));
    }

    private void DrawAuthStartScreen()
    {
        using var panel = ImRaii.Child("auth-start", new Vector2(-1f, -1f), false);
        if (!panel.Success)
        {
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;
        using (var hero = ImRaii.Child("auth-hero", new Vector2(-1f, this.Scale(164f)), false))
        {
            if (hero.Success)
            {
                var draw = ImGui.GetWindowDrawList();
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(width, this.Scale(164f));
                draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(28f));
                draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), this.Scale(28f));
                draw.AddCircleFilled(min + new Vector2(width - this.Scale(42f), this.Scale(26f)), this.Scale(54f), ImGui.GetColorU32(new Vector4(0.96f, 0.72f, 0.45f, 0.12f)), 48);
                ImGui.Dummy(new Vector2(0f, this.Scale(12f)));
                ImGui.TextUnformatted("Welcome");
                ImGui.TextWrapped("Sign in or create your TomestonePhone account before using messages, calls, contacts, and support.");
                ImGui.Spacing();
                ImGui.TextDisabled("Your account and phone number are restored automatically on this device once you sign in.");
            }
        }

        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        using (var account = ImRaii.Child("auth-account-card", new Vector2(-1f, this.Scale(172f)), false))
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

        using (var legal = ImRaii.Child("auth-legal-card", new Vector2(-1f, this.Scale(86f)), false))
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
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = this.Scale(12f);
        var cell = (availableWidth - (spacing * 2f)) / 3f;

        this.DrawAppIcon("Messages", "M", PhoneTab.Messages, this.state.UnreadConversationCount, cell, new Vector4(0.28f, 0.6f, 0.98f, 1f), new Vector4(0.17f, 0.36f, 0.8f, 1f));
        ImGui.SameLine(0f, spacing);
        this.DrawAppIcon("Calls", "C", PhoneTab.Calls, this.state.MissedCallCount, cell, new Vector4(0.23f, 0.83f, 0.57f, 1f), new Vector4(0.12f, 0.56f, 0.37f, 1f));
        ImGui.SameLine(0f, spacing);
        this.DrawAppIcon("Contacts", "P", PhoneTab.Contacts, 0, cell, new Vector4(0.98f, 0.62f, 0.39f, 1f), new Vector4(0.86f, 0.43f, 0.22f, 1f));
        this.DrawAppIcon("Friends", "F", PhoneTab.Friends, this.state.FriendRequests.Count(item => item.Status == FriendRequestStatus.Pending), cell, new Vector4(0.51f, 0.67f, 1f, 1f), new Vector4(0.27f, 0.44f, 0.85f, 1f));
        ImGui.SameLine(0f, spacing);
        this.DrawAppIcon("Settings", "S", PhoneTab.Settings, 0, cell, new Vector4(0.76f, 0.79f, 0.86f, 1f), new Vector4(0.47f, 0.52f, 0.63f, 1f));
        ImGui.SameLine(0f, spacing);
        this.DrawAppIcon("Legal", "L", PhoneTab.Legal, 0, cell, new Vector4(0.93f, 0.85f, 0.62f, 1f), new Vector4(0.74f, 0.61f, 0.29f, 1f));
        this.DrawAppIcon("Privacy", "P", PhoneTab.Privacy, 0, cell, new Vector4(0.71f, 0.62f, 0.98f, 1f), new Vector4(0.43f, 0.34f, 0.8f, 1f));
        ImGui.SameLine(0f, spacing);
        this.DrawAppIcon("Support", "?", PhoneTab.Support, this.state.SupportTickets.Count(item => item.Status == SupportTicketStatus.Open), cell, new Vector4(0.36f, 0.87f, 0.88f, 1f), new Vector4(0.2f, 0.59f, 0.64f, 1f));
        if (this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator)
        {
            ImGui.SameLine(0f, spacing);
            this.DrawAppIcon("Staff", "A", PhoneTab.Staff, this.state.VisibleReports.Count(item => item.Status == ReportStatus.Open), cell, new Vector4(0.98f, 0.46f, 0.46f, 1f), new Vector4(0.75f, 0.24f, 0.28f, 1f));
        }

        var remainingHeight = ImGui.GetContentRegionAvail().Y - this.Scale(102f);
        if (remainingHeight > 0f)
        {
            ImGui.Dummy(new Vector2(0f, remainingHeight));
        }

        this.DrawDock();
    }

    private void DrawAppIcon(string label, string glyph, PhoneTab tab, int badgeCount, float width, Vector4 topColor, Vector4 bottomColor)
    {
        var scale = this.GetUiScale();
        var cardHeight = this.Scale(116f);
        using var group = ImRaii.Child($"app-{label}", new Vector2(width, cardHeight), false);
        if (!group.Success)
        {
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var iconSize = this.Scale(72f);
        var iconMin = pos + new Vector2((width - iconSize) * 0.5f, this.Scale(6f));
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var cardMin = pos + new Vector2(this.Scale(4f), this.Scale(2f));
        var cardMax = pos + new Vector2(width - this.Scale(4f), cardHeight - this.Scale(6f));
        draw.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), this.Scale(26f));
        draw.AddRect(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.055f)), this.Scale(26f));
        draw.AddRectFilledMultiColor(iconMin, iconMax, ImGui.GetColorU32(topColor), ImGui.GetColorU32(topColor), ImGui.GetColorU32(bottomColor), ImGui.GetColorU32(bottomColor));
        draw.AddRect(iconMin, iconMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), this.Scale(22f), ImDrawFlags.None, 1.2f);
        var glyphSize = ImGui.CalcTextSize(glyph);
        draw.AddText(new Vector2(iconMin.X + (iconSize - glyphSize.X) * 0.5f, iconMin.Y + (iconSize - glyphSize.Y) * 0.5f - this.Scale(1f)), ImGui.GetColorU32(Vector4.One), glyph);

        if (badgeCount > 0)
        {
            var badgeCenter = new Vector2(iconMax.X - this.Scale(2f), iconMin.Y + this.Scale(6f));
            draw.AddCircleFilled(badgeCenter, this.Scale(14f), ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.25f, 1f)));
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeTextSize = ImGui.CalcTextSize(badgeText);
            draw.AddText(new Vector2(badgeCenter.X - badgeTextSize.X * 0.5f, badgeCenter.Y - badgeTextSize.Y * 0.5f), ImGui.GetColorU32(Vector4.One), badgeText);
        }

        if (ImGui.InvisibleButton($"{label}##open", new Vector2(width, cardHeight)))
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }

        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(pos.X + (width - labelSize.X) * 0.5f, pos.Y + this.Scale(88f)), ImGui.GetColorU32(Vector4.One), label);
    }

    private void DrawMessages()
    {
        if (this.selectedConversationId is { } selectedId && this.selectedConversationMessages is not null)
        {
            var detailHeight = this.selectedConversationDetail is null ? 0f : this.Scale(132f);
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
                        return;
                    }

                    ImGui.TextDisabled(this.selectedConversationDetail.Name);
                    ImGui.TextWrapped(string.Join(", ", this.selectedConversationDetail.Members.Select(item => $"{item.DisplayName} [{item.Role}]")));
                    if (this.selectedConversationDetail.IsGroup && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var addButtonWidth = this.Scale(104f);
                        var addInputWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X - addButtonWidth - this.Scale(10f));
                        ImGui.SetNextItemWidth(addInputWidth);
                        ImGui.InputTextWithHint("##group-add-target", "Add by username or phone number", ref this.groupAddTarget, 64);
                        ImGui.SameLine();
                        if (ImGui.Button("Add Member", new Vector2(addButtonWidth, this.Scale(30f))) && !string.IsNullOrWhiteSpace(this.groupAddTarget))
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
                                        this.RefreshSnapshot();
                                        this.groupAddTarget = string.Empty;
                                        this.pendingStatus = "Member added";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                this.pendingStatus = ex.Message;
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
                            try
                            {
                                var call = this.client.StartCallAsync(this.configuration.AuthToken, new StartCallRequest(selectedId, false)).GetAwaiter().GetResult();
                                this.state.ActiveCall = new ActiveCallState { CallId = call.Id, Title = call.DisplayName, Participants = [call.DisplayName], IsIncoming = false, IsMuted = false, StartedUtc = DateTimeOffset.UtcNow };
                                this.activeTab = PhoneTab.Calls;
                                this.showHomeScreen = false;
                                this.pendingStatus = $"Calling {call.DisplayName}";
                            }
                            catch (Exception ex)
                            {
                                this.pendingStatus = ex.Message;
                            }
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

            return;
        }

        using (var compose = ImRaii.Child("messages-compose-card", new Vector2(-1f, this.Scale(144f)), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (compose.Success)
            {
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
                        this.renderedMessageCount = 0;
                        this.scrollMessagesToBottom = true;
                        this.pendingStatus = "Conversation ready";
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = ex.Message;
                    }
                }

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
        var listHeight = Math.Max(this.Scale(180f), ImGui.GetContentRegionAvail().Y);
        using (var list = ImRaii.Child("messages-list-card", new Vector2(-1f, listHeight), true))
        {
            if (!list.Success)
            {
                return;
            }
            ImGui.TextDisabled("Recent Conversations");
            if (this.state.Conversations.Count == 0)
            {
                ImGui.TextDisabled("No conversations yet");
                ImGui.TextWrapped("Start a chat with any username or phone number above.");
                return;
            }
            foreach (var conversation in this.state.Conversations.OrderByDescending(item => item.LastActivityUtc))
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
                        var call = this.client.StartCallAsync(this.configuration.AuthToken, new StartCallRequest(conversation.Id, false)).GetAwaiter().GetResult();
                        this.state.ActiveCall = new ActiveCallState { CallId = call.Id, Title = call.DisplayName, Participants = [call.DisplayName], IsIncoming = false, IsMuted = false, StartedUtc = DateTimeOffset.UtcNow };
                        this.callTarget = string.Empty;
                        this.pendingStatus = $"Calling {call.DisplayName}";
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = ex.Message;
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
            using var item = ImRaii.Child($"call-{call.Id}", new Vector2(-1f, this.Scale(82f)), true);
            if (!item.Success)
            {
                continue;
            }
            ImGui.TextUnformatted(call.DisplayName);
            ImGui.TextDisabled($"{call.Kind}  {(call.Missed ? "Missed" : "Completed")}");
            ImGui.TextDisabled($"{call.StartedUtc.LocalDateTime:g}  Duration {call.Duration:mm\\:ss}");
        }
    }
    private void DrawContacts()
    {
        using (var add = ImRaii.Child("contacts-add-card", new Vector2(-1f, this.Scale(96f)), false))
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
                            var contact = this.client.AddContactAsync(this.configuration.AuthToken, otherMember.AccountId, otherMember.DisplayName, string.Empty).GetAwaiter().GetResult();
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
            using var item = ImRaii.Child($"contact-{contact.Id}", new Vector2(-1f, this.Scale(98f)), true);
            if (!item.Success)
            {
                continue;
            }
            ImGui.TextUnformatted(contact.DisplayName);
            if (!string.IsNullOrWhiteSpace(contact.Note))
            {
                ImGui.TextDisabled(contact.Note);
            }
            var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
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
                    var call = this.client.StartCallAsync(this.configuration.AuthToken, new StartCallRequest(conversation.Id, false)).GetAwaiter().GetResult();
                    this.state.ActiveCall = new ActiveCallState { CallId = call.Id, Title = call.DisplayName, Participants = [call.DisplayName], IsIncoming = false, IsMuted = false, StartedUtc = DateTimeOffset.UtcNow };
                    this.showHomeScreen = false;
                    this.activeTab = PhoneTab.Calls;
                    this.pendingStatus = $"Calling {call.DisplayName}";
                }
                catch (Exception ex)
                {
                    this.pendingStatus = ex.Message;
                }
            }
        }
    }

    private void DrawFriends()
    {
        using (var request = ImRaii.Child("friends-request-card", new Vector2(-1f, this.Scale(122f)), false))
        {
            if (request.Success)
            {
                ImGui.TextDisabled("Send Friend Request");
                ImGui.InputTextWithHint("##friend-target", "Username or phone number", ref this.friendRequestTarget, 64);
                ImGui.InputTextWithHint("##friend-message", "Message", ref this.friendRequestMessage, 128);
                if (ImGui.Button("Send Request", this.Scale(126f, 32f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.friendRequestTarget))
                {
                    try
                    {
                        this.client.CreateFriendRequestAsync(this.configuration.AuthToken, new FriendRequestCreateRequest(this.friendRequestTarget, string.IsNullOrWhiteSpace(this.friendRequestMessage) ? null : this.friendRequestMessage)).GetAwaiter().GetResult();
                        this.friendRequestTarget = string.Empty;
                        this.friendRequestMessage = string.Empty;
                        this.pendingStatus = "Friend request sent";
                    }
                    catch (Exception ex)
                    {
                        this.pendingStatus = ex.Message;
                    }
                }
            }
        }

        using (var pending = ImRaii.Child("friends-pending-card", new Vector2(-1f, this.Scale(182f)), false))
        {
            if (pending.Success)
            {
                ImGui.TextDisabled("Pending Requests");
                if (this.state.FriendRequests.Count == 0)
                {
                    ImGui.TextDisabled("No pending requests");
                }
                foreach (var request in this.state.FriendRequests)
                {
                    using var item = ImRaii.Child($"friend-{request.Id}", new Vector2(-1f, this.Scale(78f)), true);
                    if (!item.Success)
                    {
                        continue;
                    }
                    ImGui.TextUnformatted(request.DisplayName);
                    ImGui.TextDisabled(request.Status.ToString());
                    var actionWidth = (ImGui.GetContentRegionAvail().X - this.Scale(12f)) * 0.5f;
                    if (ImGui.Button($"Accept##{request.Id}", new Vector2(actionWidth, this.Scale(28f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, true)).GetAwaiter().GetResult();
                        if (updated is not null)
                        {
                            this.RefreshSnapshot();
                            this.pendingStatus = "Friend added";
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Decline##{request.Id}", new Vector2(actionWidth, this.Scale(28f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, false)).GetAwaiter().GetResult();
                        if (updated is not null)
                        {
                            this.RefreshSnapshot();
                            this.pendingStatus = "Request declined";
                        }
                    }
                }
            }
        }

        using var friends = ImRaii.Child("friends-list-card", new Vector2(-1f, 0f), true);
        if (!friends.Success)
        {
            return;
        }
        ImGui.TextDisabled("Friends");
        if (this.state.Friends.Count == 0)
        {
            ImGui.TextDisabled("No friends added");
            return;
        }
        foreach (var friend in this.state.Friends.OrderBy(item => item.FriendDisplayName))
        {
            using var item = ImRaii.Child($"friendship-{friend.FriendAccountId}", new Vector2(-1f, this.Scale(82f)), true);
            if (!item.Success)
            {
                continue;
            }
            ImGui.TextUnformatted(friend.FriendDisplayName);
            ImGui.TextDisabled($"Added {friend.SinceUtc.LocalDateTime:d}");
            if (ImGui.Button($"Remove##{friend.FriendAccountId}", this.Scale(108f, 30f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var removed = this.client.RemoveFriendAsync(this.configuration.AuthToken, friend.FriendAccountId).GetAwaiter().GetResult();
                if (removed)
                {
                    this.RefreshSnapshot();
                    this.pendingStatus = "Friend removed";
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
            foreach (var blockedContact in this.state.BlockedContacts.Take(3))
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

    private void AnnounceDebugOnce(string message, Exception? ex = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? "TomestonePhone error." : message.Trim();
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
    private void RefreshSnapshot()
    {
        this.QueueSnapshotRefresh();
    }

    private void QueueSnapshotRefresh()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        if (this.pendingAuthTask is { IsCompleted: false } || this.pendingSnapshotTask is { IsCompleted: false })
        {
            return;
        }

        var authToken = this.configuration.AuthToken!;
        var identity = this.GetCurrentGameIdentity();
        this.refreshOnNextDraw = false;
        this.pendingStatus = "Refreshing account...";
        this.pendingSnapshotTask = this.LoadPostAuthSnapshotAsync(authToken, identity);
    }

    private void HandleAuthFailure(Exception ex)
    {
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

        this.pendingStatus = string.IsNullOrWhiteSpace(ex.Message) ? "Authentication failed" : ex.Message;
        this.AnnounceDebugOnce($"Auth failure: {this.pendingStatus}", ex);
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
                    this.state.ActiveCall.IsIncoming = false;
                    this.state.ActiveCall.StartedUtc = DateTimeOffset.UtcNow;
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
        using var callBar = ImRaii.Child("call-banner", new Vector2(-1f, 70f), true);
        if (!callBar.Success)
        {
            return;
        }

        ImGui.TextUnformatted(call.IsIncoming ? $"Incoming Call: {call.Title}" : $"On Call: {call.Title}");
        ImGui.TextDisabled(string.Join(", ", call.Participants));

        var elapsed = call.IsIncoming ? "Ringing..." : $"{(DateTimeOffset.UtcNow - call.StartedUtc):mm\\:ss}";
        ImGui.TextDisabled(elapsed);
        ImGui.SameLine();

        if (call.IsIncoming)
        {
            if (ImGui.Button("Accept", new Vector2(70f, 24f)))
            {
                call.IsIncoming = false;
                call.StartedUtc = DateTimeOffset.UtcNow;
                this.DismissIncomingCallNotifications();
                this.activeTab = PhoneTab.Calls;
                this.showHomeScreen = false;
            }

            ImGui.SameLine();
        }

        var muteLabel = call.IsMuted ? "Unmute" : "Mute";
        if (ImGui.Button(muteLabel, new Vector2(70f, 24f)))
        {
            call.IsMuted = !call.IsMuted;
        }

        ImGui.SameLine();

        if (ImGui.Button("End", new Vector2(70f, 24f)))
        {
            this.state.ActiveCall = null;
        }
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
        if (currentSize.X <= 0 || currentSize.Y <= 0)
        {
            return;
        }

        if (Vector2.DistanceSquared(currentSize, this.lastWindowSize) < 1f)
        {
            return;
        }

        var widthChanged = Math.Abs(currentSize.X - this.lastWindowSize.X) >= Math.Abs(currentSize.Y - this.lastWindowSize.Y);
        var corrected = widthChanged
            ? new Vector2(currentSize.X, currentSize.X / PhoneAspectRatio)
            : new Vector2(currentSize.Y * PhoneAspectRatio, currentSize.Y);

        this.lastWindowSize = corrected;
        ImGui.SetWindowSize(corrected);
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
        drawList.AddRectFilledMultiColor(
            screenMin,
            screenMax,
            ImGui.GetColorU32(new Vector4(0.14f, 0.16f, 0.34f, 1f)),
            ImGui.GetColorU32(new Vector4(0.19f, 0.14f, 0.36f, 1f)),
            ImGui.GetColorU32(new Vector4(0.03f, 0.08f, 0.18f, 1f)),
            ImGui.GetColorU32(new Vector4(0.04f, 0.11f, 0.18f, 1f)));
        drawList.AddRect(screenMin, screenMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), this.Scale(36f), ImDrawFlags.None, 1f);
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.76f, windowSize.Y * 0.2f), windowSize.X * 0.45f, ImGui.GetColorU32(new Vector4(0.98f, 0.72f, 0.42f, 0.11f)), 80);
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.18f, windowSize.Y * 0.58f), windowSize.X * 0.34f, ImGui.GetColorU32(new Vector4(0.27f, 0.82f, 0.96f, 0.08f)), 80);
        drawList.AddRectFilled(screenMin + new Vector2(0f, this.Scale(12f)), screenMax - new Vector2(0f, windowSize.Y * 0.68f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.02f)), this.Scale(28f));

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

    private void DrawDock()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos();
        var dockHeight = this.Scale(90f);
        var draw = ImGui.GetWindowDrawList();
        var fill = new Vector4(1f, 1f, 1f, 0.08f);
        var border = new Vector4(1f, 1f, 1f, 0.11f);
        draw.AddRectFilled(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(fill), this.Scale(30f));
        draw.AddRect(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(border), this.Scale(30f));
        draw.AddRectFilled(start + new Vector2(this.Scale(10f), this.Scale(10f)), start + new Vector2(width - this.Scale(10f), dockHeight - this.Scale(42f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.03f)), this.Scale(22f));
        ImGui.Dummy(new Vector2(width, dockHeight));
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
        using (var compose = ImRaii.Child("support-compose-card", new Vector2(-1f, this.Scale(214f)), false))
        {
            if (compose.Success)
            {
                ImGui.TextDisabled("Support");
                ImGui.TextWrapped("Open a ticket if you need help with account issues, moderation follow-up, or app problems.");
                ImGui.InputText("Subject", ref this.supportSubject, 96);
                ImGui.InputTextMultiline("Body", ref this.supportBody, 512, new Vector2(-1f, this.Scale(92f)));
                if (ImGui.Button("Open Support Ticket", new Vector2(this.Scale(176f), this.Scale(36f))) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var ticket = this.client.CreateSupportTicketAsync(this.configuration.AuthToken, new CreateSupportTicketRequest(this.supportSubject, this.supportBody, false)).GetAwaiter().GetResult();
                    this.state.SupportTickets.Insert(0, ticket);
                    this.pendingStatus = "Support ticket opened";
                    this.supportSubject = string.Empty;
                    this.supportBody = string.Empty;
                }
            }
        }

        using (var tickets = ImRaii.Child("support-ticket-list", new Vector2(-1f, 0f), true))
        {
            if (!tickets.Success)
            {
                return;
            }
            ImGui.TextDisabled("Recent Tickets");
            if (this.state.SupportTickets.Count == 0)
            {
                ImGui.TextDisabled("No support tickets yet");
            }
            foreach (var ticket in this.state.SupportTickets.OrderByDescending(item => item.CreatedAtUtc))
            {
                using var item = ImRaii.Child($"ticket-{ticket.Id}", new Vector2(-1f, this.Scale(88f)), true);
                if (!item.Success)
                {
                    continue;
                }
                ImGui.TextUnformatted(ticket.Subject);
                ImGui.TextDisabled($"{ticket.Status}  {ticket.CreatedAtUtc.LocalDateTime:g}");
                ImGui.TextWrapped(ticket.Body);
            }
        }
    }

    private void DrawStaffApp()
    {
        if (this.state.CurrentProfile.Role is not (AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator))
        {
            ImGui.TextDisabled("Staff access only.");
            return;
        }

        using (var summary = ImRaii.Child("staff-summary-card", new Vector2(-1f, this.Scale(86f)), false))
        {
            if (summary.Success)
            {
                ImGui.TextDisabled("Staff Console");
                ImGui.TextWrapped("Review accounts, reports, and audit activity from a single staff view.");
                if (ImGui.Button("Refresh Staff Data", this.Scale(156f, 34f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    this.adminDashboard = this.client.GetAdminDashboardAsync(this.configuration.AuthToken).GetAwaiter().GetResult();
                }
            }
        }

        var dashboard = this.adminDashboard;
        if (dashboard is null)
        {
            ImGui.TextDisabled("Refresh the staff console to load accounts, reports, and audit history.");
            return;
        }

        using (var accounts = ImRaii.Child("staff-accounts-card", new Vector2(-1f, this.Scale(178f)), false))
        {
            if (accounts.Success)
            {
                ImGui.TextDisabled("Accounts");
                foreach (var account in dashboard.Accounts.Take(4))
                {
                    using var item = ImRaii.Child($"admin-account-{account.AccountId}", new Vector2(-1f, this.Scale(62f)), true);
                    if (!item.Success)
                    {
                        continue;
                    }
                    ImGui.TextUnformatted($"{account.Username} ({account.Role}, {account.Status})");
                    ImGui.TextDisabled(account.PhoneNumber);
                    ImGui.TextDisabled(string.Join(", ", account.KnownIpAddresses));
                }
            }
        }

        using (var reports = ImRaii.Child("staff-reports-card", new Vector2(-1f, this.Scale(224f)), false))
        {
            if (reports.Success)
            {
                ImGui.TextDisabled("Reports");
                foreach (var report in dashboard.Reports.Take(4))
                {
                    using var item = ImRaii.Child($"report-{report.Id}", new Vector2(-1f, this.Scale(84f)), true);
                    if (!item.Success)
                    {
                        continue;
                    }
                    ImGui.TextUnformatted($"{report.Category} [{report.Status}]");
                    ImGui.TextDisabled($"Reporter: {report.ReporterDisplayName}");
                    ImGui.TextWrapped(report.Reason);
                    if (ImGui.Button($"Open Case Thread##{report.Id}", this.Scale(146f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                    {
                        var result = this.client.ReplyToReportAsync(this.configuration.AuthToken, new ReportReplyRequest(report.Id, string.IsNullOrWhiteSpace(this.reportReplyBody) ? "Staff case thread opened." : this.reportReplyBody, true)).GetAwaiter().GetResult();
                        this.pendingStatus = result is null ? "Case thread failed" : $"Case thread {result.ConversationId}";
                    }
                }
                ImGui.InputTextMultiline("Staff Reply Body", ref this.reportReplyBody, 512, new Vector2(-1f, this.Scale(72f)));
            }
        }

        using (var audits = ImRaii.Child("staff-audit-card", new Vector2(-1f, this.Scale(154f)), false))
        {
            if (audits.Success)
            {
                ImGui.TextDisabled("Audit Logs");
                foreach (var log in dashboard.AuditLogs.Take(4))
                {
                    ImGui.TextWrapped($"{log.CreatedAtUtc.LocalDateTime:g}  {log.EventType}  {log.Summary}");
                }
            }
        }

        if (this.state.CurrentProfile.Role == AccountRole.Owner)
        {
            using var owner = ImRaii.Child("staff-owner-card", new Vector2(-1f, this.Scale(136f)), false);
            if (owner.Success)
            {
                ImGui.TextDisabled("Owner Password Reset");
                ImGui.InputText("Target Account Id", ref this.ownerResetTarget, 64);
                ImGui.InputText("New Owner Password", ref this.ownerResetPassword, 64, ImGuiInputTextFlags.Password);
                if (ImGui.Button("Reset Account Password", this.Scale(184f, 34f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && Guid.TryParse(this.ownerResetTarget, out var targetAccountId))
                {
                    var success = this.client.ResetPasswordAsOwnerAsync(this.configuration.AuthToken, new AdminPasswordResetRequest(targetAccountId, this.ownerResetPassword)).GetAwaiter().GetResult();
                    this.pendingStatus = success ? "Owner reset complete" : "Owner reset failed";
                }
            }
        }
    }

    private void DrawLegalModal()
    {
        if (!this.HasAcceptedLocalTerms())
        {
            ImGui.OpenPopup("TomestonePhone Legal Terms");
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620f, 560f), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone Legal Terms", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped(LegalTerms.Summary);
            ImGui.Separator();
            using var child = ImRaii.Child("legal-scroll", new Vector2(0f, 390f), true);
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

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620f, 560f), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("TomestonePhone Privacy Policy", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped(PrivacyPolicy.Summary);
            ImGui.Separator();
            using var child = ImRaii.Child("privacy-scroll", new Vector2(0f, 390f), true);
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

    private void DrawMessageBubble(ChatMessageRecord message)
    {
        var isSender = string.Equals(message.SenderDisplayName, this.state.CurrentProfile.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.SenderDisplayName, this.state.CurrentProfile.Username, StringComparison.OrdinalIgnoreCase);
        var bubbleWidth = Math.Max(this.Scale(140f), ImGui.GetContentRegionAvail().X * 0.76f);
        var bubblePadding = this.Scale(12f, 10f);
        var bubbleInnerWidth = Math.Max(this.Scale(96f), bubbleWidth - bubblePadding.X * 2f);
        var displayBody = message.IsDeletedForUsers ? "[Removed]" : message.Body ?? string.Empty;
        var textHeight = string.IsNullOrWhiteSpace(displayBody) ? 0f : ImGui.CalcTextSize(displayBody, false, bubbleInnerWidth).Y;
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
            if (!string.IsNullOrWhiteSpace(displayBody))
            {
                ImGui.TextUnformatted(displayBody);
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
                this.pendingStatus = string.IsNullOrWhiteSpace(ex.Message) ? "Message refresh failed" : ex.Message;
                this.AnnounceDebugOnce($"Message refresh failed: {this.pendingStatus}", ex);
            }
        }
        if (this.pendingSnapshotTask is { IsCompleted: true })
        {
            var result = this.pendingSnapshotTask.GetAwaiter().GetResult();
            this.pendingSnapshotTask = null;
            if (result.Error is not null)
            {
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
                this.pendingStatus = string.IsNullOrWhiteSpace(result.Error.Message) ? "Sync failed" : result.Error.Message;
                this.AnnounceDebugOnce($"Sync failed: {this.pendingStatus}", result.Error);
                this.SignOutToGuestState(this.pendingStatus, false, false, false);
            }
            else if (result.Snapshot is not null)
            {
                this.state.ApplySnapshot(result.Snapshot);
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

    private sealed record PostAuthSnapshotResult(PhoneSnapshot? Snapshot, PhoneProfile? UpdatedProfile, Exception? Error);}





























































