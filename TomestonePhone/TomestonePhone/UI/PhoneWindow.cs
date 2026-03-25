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
    private string friendRequestTarget = string.Empty;
    private string friendRequestMessage = string.Empty;
    private string reportReplyBody = string.Empty;
    private bool showLinkWarningModal;
    private string pendingExternalUrl = string.Empty;
    private int renderedMessageCount;
    private bool scrollMessagesToBottom = true;

    public PhoneWindow(Service service, Configuration configuration, PhoneState state, TomestonePhoneClient client)
        : base("TomestonePhone###TomestonePhoneMain")
    {
        this.service = service;
        this.configuration = configuration;
        this.state = state;
        this.client = client;
        this.gifEmbedRenderer = new GifEmbedRenderer(service.TextureProvider);
        this.Flags = ImGuiWindowFlags.NoCollapse;
        this.Size = new Vector2(440, 952);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 779f),
            MaximumSize = new Vector2(720, 1558f),
        };
        this.RespectCloseHotkey = true;
    }

    public void OpenSettingsTab()
    {
        this.showHomeScreen = false;
        this.activeTab = PhoneTab.Settings;
    }

    public void DisposeResources()
    {
        this.gifEmbedRenderer.Dispose();
    }

    public override void Draw()
    {
        this.EnforceAspectRatio();

        using var theme = PhoneTheme.Push(this.configuration);
        this.DrawPhoneShell();
        this.DrawNotifications();
        this.DrawLegalModal();
        this.DrawPrivacyModal();
        this.DrawExternalLinkWarningModal();

        using var root = ImRaii.Child("TomestonePhoneRoot", new Vector2(-1f, -1f), true);
        if (!root.Success)
        {
            return;
        }

        this.DrawCallBanner();
        this.DrawHeader();
        ImGui.Separator();

        using var content = ImRaii.Child("TomestonePhoneContent", new Vector2(-1f, -52f), true);
        if (!content.Success)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            this.DrawAuthStartScreen();
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

        this.DrawHomeButton();
    }

    private void DrawHeader()
    {
        var now = DateTime.Now.ToString("h:mm");
        ImGui.TextUnformatted(now);
        ImGui.SameLine(ImGui.GetWindowWidth() - 138f);
        ImGui.TextDisabled("5G");
        ImGui.SameLine();
        ImGui.TextDisabled("|||");
        ImGui.SameLine();
        ImGui.TextDisabled("88%");
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6f);
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            ImGui.TextUnformatted("TomestonePhone");
        }
        else
        {
            ImGui.TextUnformatted(this.showHomeScreen ? this.state.CurrentProfile.DisplayName : this.activeTab.ToString());
        }
        ImGui.TextDisabled(this.pendingStatus);
    }

    private void DrawAuthStartScreen()
    {
        using var panel = ImRaii.Child("auth-start", new Vector2(-1f, -1f), false);
        if (!panel.Success)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, 10f));
        ImGui.TextUnformatted("Welcome");
        ImGui.TextWrapped("Sign in or create your TomestonePhone account before using apps, messages, or contacts.");
        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }

        ImGui.Separator();
        {
            this.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextDisabled("Account");
        ImGui.InputText("Username", ref this.loginUsername, 64);
        ImGui.InputText("Password", ref this.loginPassword, 64, ImGuiInputTextFlags.Password);

        if (ImGui.Button("Register", new Vector2(120f, 32f)))
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

            try
            {
                var response = this.client.RegisterAsync(this.loginUsername, this.loginPassword).GetAwaiter().GetResult();
                this.configuration.Username = response.Username;
                this.configuration.AuthToken = response.AuthToken;
                this.pendingStatus = "Account created";
                this.RefreshSnapshot();
                this.SaveConfiguration();
                this.showHomeScreen = true;
            }
            catch (Exception ex)
            {
                this.HandleAuthFailure(ex);
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Login", new Vector2(120f, 32f)))
        {
            if (this.configuration.LocalAccountLockout)
            {
                this.pendingStatus = "This computer is locked";
                return;
            }

            try
            {
                var response = this.client.LoginAsync(this.loginUsername, this.loginPassword).GetAwaiter().GetResult();
                this.configuration.Username = response.Username;
                this.configuration.AuthToken = response.AuthToken;
                this.pendingStatus = $"Signed in as {response.Username}";
                this.RefreshSnapshot();
                this.SaveConfiguration();
                this.showHomeScreen = true;
            }
            catch (Exception ex)
            {
                this.HandleAuthFailure(ex);
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Terms", new Vector2(96f, 30f)))
        {
            this.activeTab = PhoneTab.Legal;
        }

        ImGui.SameLine();

        if (ImGui.Button("Privacy", new Vector2(96f, 30f)))
        {
            this.activeTab = PhoneTab.Privacy;
        }
    }

    private void DrawHomeScreen()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = 12f;
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

        this.DrawDock();
    }

    private void DrawAppIcon(string label, string glyph, PhoneTab tab, int badgeCount, float width, Vector4 topColor, Vector4 bottomColor)
    {
        using var group = ImRaii.Child($"app-{label}", new Vector2(width, 116f), false);
        if (!group.Success)
        {
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var iconMin = pos + new Vector2((width - 68f) * 0.5f, 6f);
        var iconMax = iconMin + new Vector2(68f, 68f);
        draw.AddRectFilledMultiColor(
            iconMin,
            iconMax,
            ImGui.GetColorU32(topColor),
            ImGui.GetColorU32(topColor),
            ImGui.GetColorU32(bottomColor),
            ImGui.GetColorU32(bottomColor));
        draw.AddRect(iconMin, iconMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f)), 20f, ImDrawFlags.None, 1.5f);
        var glyphSize = ImGui.CalcTextSize(glyph);
        draw.AddText(
            new Vector2(iconMin.X + (68f - glyphSize.X) * 0.5f, iconMin.Y + (68f - glyphSize.Y) * 0.5f - 1f),
            ImGui.GetColorU32(Vector4.One),
            glyph);

        if (badgeCount > 0)
        {
            var badgeCenter = new Vector2(iconMax.X - 2f, iconMin.Y + 6f);
            draw.AddCircleFilled(badgeCenter, 14f, ImGui.GetColorU32(new Vector4(0.85f, 0.25f, 0.21f, 1f)));
            draw.AddText(new Vector2(badgeCenter.X - 6f, badgeCenter.Y - 8f), ImGui.GetColorU32(Vector4.One), badgeCount.ToString());
        }

        if (ImGui.InvisibleButton($"{label}##open", new Vector2(width, 110f)))
        {
            this.showHomeScreen = false;
            this.activeTab = tab;
        }
        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(pos.X + (width - labelSize.X) * 0.5f, pos.Y + 84f), ImGui.GetColorU32(Vector4.One), label);
    }

    private void DrawMessages()
    {
        ImGui.TextDisabled("Conversations");
        ImGui.InputTextWithHint("##direct-target", "Username or phone number", ref this.directMessageTarget, 64);
        ImGui.SameLine();
        if (ImGui.Button("New Chat", new Vector2(96f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.directMessageTarget))
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

        ImGui.Separator();

        if (this.selectedConversationId is { } selectedId && this.selectedConversationMessages is not null)
        {
            if (ImGui.Button("Back To List", new Vector2(120f, 28f)))
            {
                this.selectedConversationId = null;
                this.selectedConversationMessages = null;
                this.selectedConversationDetail = null;
            }

            ImGui.SameLine();
            ImGui.TextDisabled(this.selectedConversationDetail?.Name ?? "Conversation");
            ImGui.Separator();

            if (this.selectedConversationDetail is not null)
            {
                ImGui.TextDisabled("Members");
                ImGui.TextWrapped(string.Join(", ", this.selectedConversationDetail.Members.Select(item => $"{item.DisplayName} [{item.Role}]")));
                if (!this.selectedConversationDetail.IsGroup && this.selectedConversationDetail.Members.FirstOrDefault(item => item.AccountId != this.state.CurrentProfile.AccountId) is { } otherMember && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    if (ImGui.Button("Add As Contact", new Vector2(130f, 28f)))
                    {
                        var contact = this.client.AddContactAsync(this.configuration.AuthToken, otherMember.AccountId, otherMember.DisplayName, string.Empty).GetAwaiter().GetResult();
                        this.state.Contacts.RemoveAll(item => item.Id == contact.Id);
                        this.state.Contacts.Add(contact);
                        this.pendingStatus = "Contact saved";
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Block", new Vector2(90f, 28f)))
                    {
                        var success = this.client.BlockAccountAsync(this.configuration.AuthToken, otherMember.AccountId).GetAwaiter().GetResult();
                        this.pendingStatus = success ? "Blocked" : "Block failed";
                    }
                }
                ImGui.Separator();
            }

            using (var scroll = ImRaii.Child("message-thread", new Vector2(-1f, -88f), true, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (scroll.Success)
                {
                    var currentMessages = this.selectedConversationMessages!.Messages;
                    var currentCount = currentMessages.Count;
                    if (currentCount != this.renderedMessageCount)
                    {
                        this.renderedMessageCount = currentCount;
                        this.scrollMessagesToBottom = true;
                    }

                    foreach (var message in this.selectedConversationMessages.Messages)
                    {
                        this.DrawMessageBubble(message);
                    }

                    if (this.scrollMessagesToBottom)
                    {
                        ImGui.SetScrollHereY(1f);
                        this.scrollMessagesToBottom = false;
                    }
                }
            }

            ImGui.Separator();
            ImGui.InputTextWithHint("##gif-url", "Optional direct GIF URL", ref this.composeEmbedUrl, 512);
            ImGui.TextDisabled("Direct .gif links render inline in chat");

            var composerWidth = Math.Max(120f, ImGui.GetContentRegionAvail().X - 84f);
            var inputFlags = ImGuiInputTextFlags.EnterReturnsTrue;
            var submitted = ImGui.InputTextMultiline("##message-compose", ref this.composeMessage, 1024, new Vector2(composerWidth, 62f), inputFlags);
            if (submitted)
            {
                if (ImGui.GetIO().KeyShift)
                {
                    this.composeMessage += Environment.NewLine;
                    ImGui.SetKeyboardFocusHere(-1);
                }
                else
                {
                    this.SendComposedMessage(selectedId);
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Send", new Vector2(72f, 34f)))
            {
                this.SendComposedMessage(selectedId);
            }

            if (this.selectedConversationDetail is not null && this.selectedConversationDetail.IsGroup && this.state.CurrentProfile.Role is AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator)
            {
                ImGui.SameLine();
                if (ImGui.Button("Promote First Member", new Vector2(150f, 30f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
                {
                    var target = this.selectedConversationDetail.Members.FirstOrDefault(item => item.Role == GroupMemberRole.Member);
                    if (target is not null)
                    {
                        this.selectedConversationDetail = this.client.ModerateConversationAsync(this.configuration.AuthToken, new ConversationModerationRequest(selectedId, ChatModerationAction.PromoteModerator, target.AccountId)).GetAwaiter().GetResult();
                    }
                }
            }

            return;
        }

        if (this.state.Conversations.Count == 0)
        {
            ImGui.TextDisabled("No conversations yet");
            ImGui.TextWrapped("Start a chat with any username or phone number above");
            return;
        }

        foreach (var conversation in this.state.Conversations.OrderByDescending(item => item.LastActivityUtc))
        {
            using var item = ImRaii.Child($"conversation-{conversation.Id}", new Vector2(-1f, 72f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(conversation.DisplayName);
            ImGui.SameLine();
            if (conversation.UnreadCount > 0)
            {
                ImGui.TextColored(new Vector4(0.85f, 0.71f, 0.43f, 1f), $"[{conversation.UnreadCount}]");
            }

            ImGui.TextDisabled(conversation.LastMessagePreview);
            ImGui.TextDisabled($"{conversation.LastActivityUtc.LocalDateTime:t} {(conversation.IsGroup ? "Group" : "Direct")}");
            if (ImGui.Button($"Open##{conversation.Id}", new Vector2(72f, 24f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                this.selectedConversationId = conversation.Id;
                this.selectedConversationMessages = this.client.GetConversationMessagesAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                this.selectedConversationDetail = this.client.GetConversationDetailAsync(this.configuration.AuthToken, conversation.Id).GetAwaiter().GetResult();
                this.renderedMessageCount = 0;
                this.scrollMessagesToBottom = true;
                this.DismissNotificationsFor(conversation.Id);
            }
        }
    }

    private void DrawCalls()
    {
        ImGui.TextDisabled("Recent Calls");
        ImGui.Separator();

        if (this.state.RecentCalls.Count == 0)
        {
            ImGui.TextDisabled("No calls yet");
            return;
        }

        foreach (var call in this.state.RecentCalls.OrderByDescending(item => item.StartedUtc))
        {
            using var item = ImRaii.Child($"call-{call.Id}", new Vector2(-1f, 68f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(call.DisplayName);
            ImGui.TextDisabled($"{call.Kind} call");
            ImGui.TextDisabled($"{call.StartedUtc.LocalDateTime:g}  Duration: {call.Duration:mm\\:ss}  {(call.Missed ? "Missed" : "Completed")}");
        }
    }

    private void DrawContacts()
    {
        ImGui.TextDisabled("Contacts");
        ImGui.Separator();

        if (this.state.Contacts.Count == 0)
        {
            ImGui.TextDisabled("No contacts saved");
            return;
        }

        foreach (var contact in this.state.Contacts.OrderBy(item => item.DisplayName))
        {
            using var item = ImRaii.Child($"contact-{contact.Id}", new Vector2(-1f, 72f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(contact.DisplayName);
            if (!string.IsNullOrWhiteSpace(contact.Note))
            {
                ImGui.TextDisabled(contact.Note);
            }
        }
    }

    private void DrawFriends()
    {
        ImGui.TextDisabled("Send Friend Request");
        ImGui.InputTextWithHint("##friend-target", "Username or phone number", ref this.friendRequestTarget, 64);
        ImGui.InputTextWithHint("##friend-message", "Message", ref this.friendRequestMessage, 128);
        if (ImGui.Button("Send Request", new Vector2(120f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && !string.IsNullOrWhiteSpace(this.friendRequestTarget))
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

        ImGui.Separator();
        ImGui.TextDisabled("Pending Requests");
        ImGui.Separator();

        if (this.state.FriendRequests.Count == 0)
        {
            ImGui.TextDisabled("No pending requests");
        }

        foreach (var request in this.state.FriendRequests)
        {
            using var item = ImRaii.Child($"friend-{request.Id}", new Vector2(-1f, 76f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(request.DisplayName);
            ImGui.TextDisabled(request.Status.ToString());
            ImGui.SameLine();
            if (ImGui.Button($"Accept##{request.Id}") && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, true)).GetAwaiter().GetResult();
                if (updated is not null)
                {
                    this.RefreshSnapshot();
                    this.pendingStatus = "Friend added";
                }
            }
            ImGui.SameLine();
            if (ImGui.Button($"Decline##{request.Id}") && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var updated = this.client.RespondToFriendRequestAsync(this.configuration.AuthToken, new RespondFriendRequest(request.Id, false)).GetAwaiter().GetResult();
                if (updated is not null)
                {
                    this.RefreshSnapshot();
                    this.pendingStatus = "Request declined";
                }
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Friends");

        if (this.state.Friends.Count == 0)
        {
            ImGui.TextDisabled("No friends added");
            return;
        }

        foreach (var friend in this.state.Friends.OrderBy(item => item.FriendDisplayName))
        {
            using var item = ImRaii.Child($"friendship-{friend.FriendAccountId}", new Vector2(-1f, 72f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(friend.FriendDisplayName);
            ImGui.TextDisabled($"Added {friend.SinceUtc.LocalDateTime:d}");
            if (ImGui.Button($"Remove##{friend.FriendAccountId}", new Vector2(100f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
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

    private void DrawSettings()
    {
        var isAuthenticated = !string.IsNullOrWhiteSpace(this.configuration.AuthToken);
        if (!isAuthenticated)
        {
            ImGui.TextDisabled("Account");
            ImGui.TextWrapped("Create an account or sign in to start using TomestonePhone");
            if (this.configuration.LocalAccountLockout)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
            }

            ImGui.Separator();
            ImGui.InputText("Username", ref this.loginUsername, 64);
            ImGui.InputText("Password", ref this.loginPassword, 64, ImGuiInputTextFlags.Password);

            if (ImGui.Button("Register", new Vector2(120f, 32f)))
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

                try
                {
                    var response = this.client.RegisterAsync(this.loginUsername, this.loginPassword).GetAwaiter().GetResult();
                    this.configuration.Username = response.Username;
                    this.configuration.AuthToken = response.AuthToken;
                    this.pendingStatus = "Account created";
                    this.RefreshSnapshot();
                    this.SaveConfiguration();
                }
                catch (Exception ex)
                {
                    this.HandleAuthFailure(ex);
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Login", new Vector2(120f, 32f)))
            {
                if (this.configuration.LocalAccountLockout)
                {
                    this.pendingStatus = "This computer is locked";
                    return;
                }

                try
                {
                    var response = this.client.LoginAsync(this.loginUsername, this.loginPassword).GetAwaiter().GetResult();
                    this.configuration.Username = response.Username;
                    this.configuration.AuthToken = response.AuthToken;
                    this.pendingStatus = $"Signed in as {response.Username}";
                    this.RefreshSnapshot();
                    this.SaveConfiguration();
                }
                catch (Exception ex)
                {
                    this.HandleAuthFailure(ex);
                }
            }

            ImGui.Separator();
            {
                this.SaveConfiguration();
            }

            ImGui.Separator();
            if (ImGui.Button("Terms", new Vector2(96f, 30f)))
            {
                this.activeTab = PhoneTab.Legal;
                return;
            }

            ImGui.SameLine();

            if (ImGui.Button("Privacy", new Vector2(96f, 30f)))
            {
                this.activeTab = PhoneTab.Privacy;
                return;
            }

            return;
        }

        ImGui.TextDisabled("Phone Number");
        if (ImGui.Selectable(this.state.CurrentProfile.PhoneNumber, false, ImGuiSelectableFlags.None, new Vector2(-1f, 24f)))
        {
            ImGui.SetClipboardText(this.state.CurrentProfile.PhoneNumber);
            this.pendingStatus = "Phone number copied";
        }
        if (this.configuration.LocalAccountLockout)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), this.configuration.LocalAccountLockoutReason);
        }
        ImGui.Separator();

        if (ImGui.Button("Terms", new Vector2(96f, 30f)))
        {
            this.activeTab = PhoneTab.Legal;
            return;
        }

        ImGui.SameLine();

        if (ImGui.Button("Privacy", new Vector2(96f, 30f)))
        {
            this.activeTab = PhoneTab.Privacy;
            return;
        }

        ImGui.SameLine();

        if (ImGui.Button("Log Out", new Vector2(96f, 30f)))
        {
            this.configuration.AuthToken = null;
            this.configuration.Username = null;
            this.pendingStatus = "Signed out";
            this.SaveConfiguration();
            return;
        }

        ImGui.Separator();
        ImGui.TextDisabled("App");
        this.DrawEditableText("Accent Color", this.configuration.AccentColorHex, value => this.configuration.AccentColorHex = value, 16);
        ImGui.TextDisabled("GIFs");
        ImGui.TextWrapped("Paste a direct .gif link into the message screen to send and display it inline");

        var lockViewport = this.configuration.LockViewport;
        if (ImGui.Checkbox("Lock viewport inside phone frame", ref lockViewport))
        {
            this.configuration.LockViewport = lockViewport;
        }

        {
            this.SaveConfiguration();
        }

        ImGui.SameLine();
        this.DrawNotificationAnchorPicker();

        ImGui.Separator();

        ImGui.TextDisabled("Account");
        var muted = this.state.CurrentProfile.NotificationsMuted;
        if (ImGui.Checkbox("Mute notifications", ref muted))
        {
            this.state.CurrentProfile = this.client.UpdateNotificationSettingsAsync(this.configuration.AuthToken!, muted).GetAwaiter().GetResult();
            this.pendingStatus = muted ? "Notifications muted" : "Notifications enabled";
        }

        if (ImGui.Button("Refresh Account", new Vector2(140f, 32f)))
        {
            this.RefreshSnapshot();
        }

        ImGui.Separator();
        ImGui.TextDisabled("Change Password");
        ImGui.InputText("Old Password", ref this.oldPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.InputText("New Password", ref this.newPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.InputText("Confirm New", ref this.confirmPassword, 64, ImGuiInputTextFlags.Password);
        if (ImGui.Button("Update Password", new Vector2(160f, 32f)))
        {
            var success = this.client.ChangePasswordAsync(this.configuration.AuthToken!, new PasswordResetSelfRequest(this.oldPassword, this.newPassword, this.confirmPassword)).GetAwaiter().GetResult();
            this.pendingStatus = success ? "Password updated" : "Password update failed";
        }

        ImGui.Separator();
        ImGui.TextDisabled("Blocked Contacts");
        if (this.state.BlockedContacts.Count == 0)
        {
            ImGui.TextDisabled("No blocked contacts");
        }
        foreach (var blocked in this.state.BlockedContacts.OrderBy(item => item.DisplayName))
        {
            using var item = ImRaii.Child($"blocked-{blocked.Id}", new Vector2(-1f, 58f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(blocked.DisplayName);
            ImGui.TextDisabled(blocked.Note);
            if (ImGui.Button($"Unblock##{blocked.Id}", new Vector2(100f, 28f)))
            {
                var success = this.client.UnblockAccountAsync(this.configuration.AuthToken!, blocked.Id).GetAwaiter().GetResult();
                if (success)
                {
                    this.state.BlockedContacts.RemoveAll(item => item.Id == blocked.Id);
                    this.pendingStatus = "Contact unblocked";
                }
            }
        }
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
            this.scrollMessagesToBottom = true;
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
        var buffer = value;
        if (ImGui.InputText(label, ref buffer, maxLength))
        {
            setter(buffer);
        }
    }

    private void RefreshSnapshot()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            return;
        }

        try
        {
            var snapshot = this.client.GetSnapshotAsync(this.configuration.AuthToken).GetAwaiter().GetResult();
            this.state.ApplySnapshot(snapshot);
            var identity = this.GetCurrentGameIdentity();
            if (identity is not null)
            {
                this.state.CurrentProfile = this.client.UpdateGameIdentityAsync(this.configuration.AuthToken, new UpdateGameIdentityRequest(identity.CharacterName, identity.WorldName)).GetAwaiter().GetResult();
            }
            if (this.state.CurrentProfile.Status == AccountStatus.Banned)
            {
                this.configuration.LocalAccountLockout = true;
                this.configuration.LocalAccountLockoutReason = "This device is locked because the linked account was banned.";
                this.configuration.AuthToken = null;
                this.configuration.Username = null;
                this.SaveConfiguration();
            }
            this.pendingStatus = $"Synced {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            this.pendingStatus = "Sync failed";
            this.service.Log.Warning(ex, "Failed to refresh TomestonePhone snapshot.");
        }
    }

    private void HandleAuthFailure(Exception ex)
    {
        var message = ex.ToString();
        if (message.Contains("403") || message.Contains("banned", StringComparison.OrdinalIgnoreCase) || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            this.configuration.LocalAccountLockout = true;
            this.configuration.LocalAccountLockoutReason = "This device is locked due to a banned account or IP restriction.";
            this.configuration.AuthToken = null;
            this.configuration.Username = null;
            this.SaveConfiguration();
            this.pendingStatus = "Device locked";
            return;
        }

        this.pendingStatus = "Authentication failed";
    }

    private void DrawHomeButton()
    {
        var size = ImGui.GetWindowSize();
        var pillSize = new Vector2(124f, 8f);
        ImGui.SetCursorPos(new Vector2((size.X - pillSize.X) * 0.5f, size.Y - 18f));
        var pos = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + pillSize, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.72f)), 999f);
        if (ImGui.InvisibleButton("Home", pillSize))
        {
            this.showHomeScreen = true;
        }
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
        var anchor = this.configuration.NotificationAnchor;
        if (ImGui.BeginCombo("Notification Spot", anchor.ToString()))
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
        var shellColor = ImGui.GetColorU32(new Vector4(0.09f, 0.1f, 0.13f, 1f));
        var trimColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
        var notchColor = ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.03f, 1f));
        var screenMin = windowPos + new Vector2(8f, 8f);
        var screenMax = windowPos + windowSize - new Vector2(8f, 8f);

        drawList.AddRectFilled(windowPos, windowPos + windowSize, shellColor, 42f);
        drawList.AddRect(windowPos, windowPos + windowSize, trimColor, 42f, ImDrawFlags.None, 1.6f);
        drawList.AddRectFilledMultiColor(
            screenMin,
            screenMax,
            ImGui.GetColorU32(new Vector4(0.16f, 0.17f, 0.35f, 1f)),
            ImGui.GetColorU32(new Vector4(0.2f, 0.12f, 0.34f, 1f)),
            ImGui.GetColorU32(new Vector4(0.04f, 0.07f, 0.17f, 1f)),
            ImGui.GetColorU32(new Vector4(0.03f, 0.08f, 0.14f, 1f)));
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.5f, windowSize.Y * 0.22f), windowSize.X * 0.42f, ImGui.GetColorU32(new Vector4(0.98f, 0.68f, 0.38f, 0.12f)), 64);
        drawList.AddCircleFilled(windowPos + new Vector2(windowSize.X * 0.22f, windowSize.Y * 0.46f), windowSize.X * 0.32f, ImGui.GetColorU32(new Vector4(0.2f, 0.78f, 0.95f, 0.09f)), 64);

        var notchWidth = windowSize.X * 0.33f;
        var notchMin = new Vector2(windowPos.X + (windowSize.X - notchWidth) * 0.5f, windowPos.Y);
        var notchMax = new Vector2(notchMin.X + notchWidth, windowPos.Y + 26f);
        drawList.AddRectFilled(notchMin, notchMax, notchColor, 12f);
    }

    private void DrawDock()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos() + new Vector2(0f, 14f);
        var dockHeight = 88f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f)), 28f);
        draw.AddRect(start, start + new Vector2(width, dockHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), 28f);
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
        ImGui.TextDisabled("Support");
        if (this.state.SupportTickets.Count == 0)
        {
            ImGui.TextDisabled("No support tickets yet");
        }
        foreach (var ticket in this.state.SupportTickets.OrderByDescending(item => item.CreatedAtUtc))
        {
            using var item = ImRaii.Child($"ticket-{ticket.Id}", new Vector2(-1f, 72f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted(ticket.Subject);
            ImGui.TextDisabled(ticket.Body);
            ImGui.TextDisabled($"{ticket.Status}  {ticket.CreatedAtUtc.LocalDateTime:g}");
        }

        ImGui.Separator();
        ImGui.InputText("Subject", ref this.supportSubject, 96);
        ImGui.InputTextMultiline("Body", ref this.supportBody, 512, new Vector2(-1f, 100f));
        if (ImGui.Button("Open Support Ticket", new Vector2(180f, 32f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            var ticket = this.client.CreateSupportTicketAsync(this.configuration.AuthToken, new CreateSupportTicketRequest(this.supportSubject, this.supportBody, false)).GetAwaiter().GetResult();
            this.state.SupportTickets.Insert(0, ticket);
            this.pendingStatus = "Support ticket opened";
            this.supportSubject = string.Empty;
            this.supportBody = string.Empty;
        }
    }

    private void DrawStaffApp()
    {
        if (this.state.CurrentProfile.Role is not (AccountRole.Owner or AccountRole.Admin or AccountRole.Moderator))
        {
            ImGui.TextDisabled("Staff access only.");
            return;
        }

        if (ImGui.Button("Refresh Staff Data", new Vector2(150f, 30f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
        {
            this.adminDashboard = this.client.GetAdminDashboardAsync(this.configuration.AuthToken).GetAwaiter().GetResult();
        }

        var dashboard = this.adminDashboard;
        if (dashboard is null)
        {
            ImGui.TextDisabled("Load the dashboard to review accounts, reports, and audit history");
            return;
        }

        ImGui.Separator();
        ImGui.TextDisabled("Accounts");
        foreach (var account in dashboard.Accounts.Take(8))
        {
            using var item = ImRaii.Child($"admin-account-{account.AccountId}", new Vector2(-1f, 70f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted($"{account.Username} ({account.Role}, {account.Status})");
            ImGui.TextDisabled(account.PhoneNumber);
            ImGui.TextDisabled(string.Join(", ", account.KnownIpAddresses));
        }

        ImGui.Separator();
        ImGui.TextDisabled("Reports");
        foreach (var report in dashboard.Reports.Take(6))
        {
            using var item = ImRaii.Child($"report-{report.Id}", new Vector2(-1f, 88f), true);
            if (!item.Success)
            {
                continue;
            }

            ImGui.TextUnformatted($"{report.Category} [{report.Status}]");
            ImGui.TextDisabled(report.Reason);
            ImGui.TextDisabled($"Reporter: {report.ReporterDisplayName}");
            if (ImGui.Button($"Open Case Thread##{report.Id}", new Vector2(150f, 28f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken))
            {
                var result = this.client.ReplyToReportAsync(this.configuration.AuthToken, new ReportReplyRequest(report.Id, string.IsNullOrWhiteSpace(this.reportReplyBody) ? "Staff case thread opened." : this.reportReplyBody, true)).GetAwaiter().GetResult();
                this.pendingStatus = result is null ? "Case thread failed" : $"Case thread {result.ConversationId}";
            }
        }

        ImGui.InputTextMultiline("Staff Reply Body", ref this.reportReplyBody, 512, new Vector2(-1f, 80f));

        ImGui.Separator();
        ImGui.TextDisabled("Audit Logs");
        foreach (var log in dashboard.AuditLogs.Take(5))
        {
            ImGui.TextWrapped($"{log.CreatedAtUtc.LocalDateTime:g}  {log.EventType}  {log.Summary}");
        }

        if (this.state.CurrentProfile.Role == AccountRole.Owner)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Owner Password Reset");
            ImGui.InputText("Target Account Id", ref this.ownerResetTarget, 64);
            ImGui.InputText("New Owner Password", ref this.ownerResetPassword, 64, ImGuiInputTextFlags.Password);
            if (ImGui.Button("Reset Account Password", new Vector2(190f, 32f)) && !string.IsNullOrWhiteSpace(this.configuration.AuthToken) && Guid.TryParse(this.ownerResetTarget, out var targetAccountId))
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
        var bubbleWidth = ImGui.GetContentRegionAvail().X * 0.76f;
        var cursorX = ImGui.GetCursorPosX();
        if (isSender)
        {
            ImGui.SetCursorPosX(Math.Max(cursorX, ImGui.GetWindowContentRegionMax().X - bubbleWidth - 12f));
        }

        var bubbleColor = isSender
            ? new Vector4(0.25f, 0.51f, 0.96f, 0.95f)
            : new Vector4(0.94f, 0.94f, 0.96f, 0.98f);
        var textColor = isSender ? Vector4.One : new Vector4(0.1f, 0.1f, 0.12f, 1f);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, bubbleColor))
        using (var bubble = ImRaii.Child($"bubble-{message.Id}", new Vector2(bubbleWidth, 0f), true))
        {
            if (bubble.Success)
            {
                using var textScope = ImRaii.PushColor(ImGuiCol.Text, textColor);
                if (!string.IsNullOrWhiteSpace(message.Body) || message.IsDeletedForUsers)
                {
                    ImGui.TextWrapped(message.IsDeletedForUsers ? "[Removed]" : message.Body);
                }

                foreach (var embed in message.Embeds)
                {
                    if (this.gifEmbedRenderer.IsGifUrl(embed.Url))
                    {
                        this.gifEmbedRenderer.Draw(embed.Url, bubbleWidth - 20f, this.IsGifAnimationActive());
                        continue;
                    }

                    using var embedScope = ImRaii.PushColor(ImGuiCol.Text, isSender ? new Vector4(0.91f, 0.96f, 1f, 1f) : new Vector4(0.13f, 0.33f, 0.78f, 1f));
                    if (ImGui.Selectable($"{embed.Url}##{embed.Id}", false))
                    {
                        this.pendingExternalUrl = embed.Url;
                        this.showLinkWarningModal = true;
                        ImGui.OpenPopup("TomestonePhone External Link");
                    }
                }
            }
        }

        if (!isSender)
        {
            ImGui.TextDisabled($"{message.SenderDisplayName}  {message.SentAtUtc.LocalDateTime:g}");
        }
        else
        {
            var meta = message.SentAtUtc.LocalDateTime.ToString("g");
            var width = ImGui.CalcTextSize(meta).X;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width - 12f));
            ImGui.TextDisabled(meta);
        }
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

}

























