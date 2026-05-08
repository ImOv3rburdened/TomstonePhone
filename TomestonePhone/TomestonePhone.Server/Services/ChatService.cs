using Microsoft.Extensions.Options;
using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class ChatService : IChatService
{
    private readonly IPhoneRepository repository;
    private readonly GroupConversationPolicyOptions groupConversationPolicy;

    public ChatService(IPhoneRepository repository, IOptions<GroupConversationPolicyOptions> groupConversationPolicy)
    {
        this.repository = repository;
        this.groupConversationPolicy = groupConversationPolicy.Value;
    }

    public Task<ConversationSummary> CreateConversationAsync(Guid ownerAccountId, CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var members = request.ParticipantIds
                .Append(ownerAccountId)
                .Distinct()
                .Select(id => new PersistedConversationMember
                {
                    AccountId = id,
                    Role = id == ownerAccountId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member),
                    JoinedAtUtc = DateTimeOffset.UtcNow,
                })
                .ToList();

            if (request.IsGroup)
            {
                this.EnsureCanCreateOrGrowStandardGroup(state, ownerAccountId, members.Count);
            }

            var conversation = new PersistedConversation
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                IsGroup = request.IsGroup,
                Kind = SystemConversationCoordinator.StandardConversationKind,
                Members = members,
            };

            state.Conversations.Add(conversation);
            return MapSummary(state, ownerAccountId, conversation);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<ConversationSummary>>(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            return state.Conversations
                .Where(item => !item.IsDeleted && ConversationMembershipPolicy.CanViewConversation(item, accountId))
                .Select(item => MapSummary(state, accountId, item))
                .OrderByDescending(item => item.LastActivityUtc)
                .ToList();
        }, cancellationToken);
    }

    public Task<ConversationDetail> GetConversationDetailAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = GetVisibleConversation(state, accountId, conversationId);
            return MapDetail(state, conversation, accountId);
        }, cancellationToken);
    }

    public Task<ConversationMessagePage> GetMessagesAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = GetVisibleConversation(state, accountId, conversationId);
            var messages = conversation.Messages
                .OrderBy(item => item.SentAtUtc)
                .Where(item => IsMessageVisibleToViewer(state, conversation, item, accountId))
                .Select(item => MapMessage(state, conversation.Id, item))
                .ToList();

            return new ConversationMessagePage(conversation.Id, messages);
        }, cancellationToken);
    }

    public Task<ConversationDetail?> ModerateConversationAsync(Guid actorAccountId, ConversationModerationRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<ConversationDetail?>(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = GetVisibleConversation(state, actorAccountId, request.ConversationId);
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            var actorMember = ConversationMembershipPolicy.FindMember(conversation, actorAccountId)
                ?? throw new InvalidOperationException("Conversation unavailable.");
            var actorRole = ParseRole(actorMember.Role);
            var isStaffActor = SystemConversationCoordinator.IsStaffRole(actor.Role);
            var now = DateTimeOffset.UtcNow;

            if (conversation.LinkedSupportTicketId is not null && !isStaffActor)
            {
                throw new InvalidOperationException("This conversation cannot be managed from here.");
            }

            if (conversation.Kind == SystemConversationCoordinator.StaffConversationKind && actor.Role != nameof(AccountRole.Owner))
            {
                throw new InvalidOperationException("Only the server owner can manage the staff room.");
            }

            if (conversation.LinkedSupportTicketId is null
                && conversation.Kind == SystemConversationCoordinator.StandardConversationKind
                && request.Action != ChatModerationAction.HideConversation
                && request.Action != ChatModerationAction.LeaveConversation
                && !ConversationMembershipPolicy.IsActiveMember(actorMember))
            {
                throw new InvalidOperationException("You are no longer an active member of this conversation.");
            }

            switch (request.Action)
            {
                case ChatModerationAction.AddMember when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } addId:
                    if (conversation.IsReadOnly)
                    {
                        throw new InvalidOperationException("This conversation is closed.");
                    }

                    if (conversation.Kind != SystemConversationCoordinator.StandardConversationKind)
                    {
                        throw new InvalidOperationException("Members cannot be added to this conversation.");
                    }

                    var existingMember = ConversationMembershipPolicy.FindMember(conversation, addId);
                    var activeMemberCount = ConversationMembershipPolicy.GetActiveMembers(conversation).Count();
                    if (existingMember is null)
                    {
                        this.EnsureCanCreateOrGrowStandardGroup(state, GetConversationOwnerAccountId(conversation), activeMemberCount + 1, conversation);
                        conversation.Members.Add(new PersistedConversationMember
                        {
                            AccountId = addId,
                            Role = nameof(GroupMemberRole.Member),
                            JoinedAtUtc = now,
                        });
                    }
                    else if (!ConversationMembershipPolicy.IsActiveMember(existingMember) || existingMember.HiddenAtUtc is not null)
                    {
                        this.EnsureCanCreateOrGrowStandardGroup(state, GetConversationOwnerAccountId(conversation), activeMemberCount + 1, conversation);
                        existingMember.Role = nameof(GroupMemberRole.Member);
                        existingMember.RemovedAtUtc = null;
                        existingMember.HiddenAtUtc = null;
                    }

                    conversation.PendingMemberRequests.RemoveAll(item => item.TargetAccountId == addId);
                    break;
                case ChatModerationAction.RequestAddMember when conversation.LinkedSupportTicketId is null && request.TargetAccountId is { } requestAddId:
                    if (conversation.IsReadOnly)
                    {
                        throw new InvalidOperationException("This conversation is closed.");
                    }

                    if (conversation.Kind != SystemConversationCoordinator.StandardConversationKind || !conversation.IsGroup)
                    {
                        throw new InvalidOperationException("Members cannot be requested for this conversation.");
                    }

                    if (actorRole == GroupMemberRole.Owner)
                    {
                        throw new InvalidOperationException("Owners can add members directly.");
                    }

                    if (requestAddId == actorAccountId)
                    {
                        throw new InvalidOperationException("You are already in this group.");
                    }

                    _ = state.Accounts.SingleOrDefault(item => item.Id == requestAddId)
                        ?? throw new InvalidOperationException("That contact could not be found.");

                    var requestedMember = ConversationMembershipPolicy.FindMember(conversation, requestAddId);
                    if (ConversationMembershipPolicy.IsActiveMember(requestedMember))
                    {
                        throw new InvalidOperationException("That contact is already in this group.");
                    }

                    if (conversation.PendingMemberRequests.Any(item => item.TargetAccountId == requestAddId))
                    {
                        throw new InvalidOperationException("That contact is already pending owner approval.");
                    }

                    conversation.PendingMemberRequests.Add(new PersistedConversationPendingMemberRequest
                    {
                        TargetAccountId = requestAddId,
                        RequestedByAccountId = actorAccountId,
                        RequestedAtUtc = now,
                    });
                    break;
                case ChatModerationAction.ApprovePendingMemberRequest when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } approveId:
                    if (conversation.IsReadOnly)
                    {
                        throw new InvalidOperationException("This conversation is closed.");
                    }

                    var pendingApproval = conversation.PendingMemberRequests.SingleOrDefault(item => item.TargetAccountId == approveId)
                        ?? throw new InvalidOperationException("That request is no longer pending.");
                    var existingApprovedMember = ConversationMembershipPolicy.FindMember(conversation, approveId);
                    var approvedActiveMemberCount = ConversationMembershipPolicy.GetActiveMembers(conversation).Count();
                    if (existingApprovedMember is null)
                    {
                        this.EnsureCanCreateOrGrowStandardGroup(state, GetConversationOwnerAccountId(conversation), approvedActiveMemberCount + 1, conversation);
                        conversation.Members.Add(new PersistedConversationMember
                        {
                            AccountId = approveId,
                            Role = nameof(GroupMemberRole.Member),
                            JoinedAtUtc = now,
                        });
                    }
                    else if (!ConversationMembershipPolicy.IsActiveMember(existingApprovedMember) || existingApprovedMember.HiddenAtUtc is not null)
                    {
                        this.EnsureCanCreateOrGrowStandardGroup(state, GetConversationOwnerAccountId(conversation), approvedActiveMemberCount + 1, conversation);
                        existingApprovedMember.Role = nameof(GroupMemberRole.Member);
                        existingApprovedMember.RemovedAtUtc = null;
                        existingApprovedMember.HiddenAtUtc = null;
                    }

                    conversation.PendingMemberRequests.RemoveAll(item => item.TargetAccountId == pendingApproval.TargetAccountId);
                    break;
                case ChatModerationAction.DeclinePendingMemberRequest when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } declineId:
                    if (conversation.PendingMemberRequests.RemoveAll(item => item.TargetAccountId == declineId) == 0)
                    {
                        throw new InvalidOperationException("That request is no longer pending.");
                    }
                    break;
                case ChatModerationAction.RemoveMember when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } removeId:
                    if (conversation.IsReadOnly)
                    {
                        throw new InvalidOperationException("This conversation is closed.");
                    }

                    if (removeId == actorAccountId)
                    {
                        throw new InvalidOperationException("Use the delete action if you want to close this group for everyone.");
                    }

                    var removeMember = ConversationMembershipPolicy.FindMember(conversation, removeId);
                    if (ConversationMembershipPolicy.IsActiveMember(removeMember))
                    {
                        removeMember!.RemovedAtUtc = now;
                        removeMember.HiddenAtUtc = null;
                    }
                    break;
                case ChatModerationAction.PromoteModerator when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } promoteId:
                    SetMemberRole(conversation, promoteId, GroupMemberRole.Moderator);
                    break;
                case ChatModerationAction.DemoteModerator when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } demoteId:
                    SetMemberRole(conversation, demoteId, GroupMemberRole.Member);
                    break;
                case ChatModerationAction.TransferOwnership when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner && request.TargetAccountId is { } transferId:
                    SetMemberRole(conversation, actorAccountId, GroupMemberRole.Moderator);
                    SetMemberRole(conversation, transferId, GroupMemberRole.Owner);
                    break;
                case ChatModerationAction.CloseConversation when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner:
                    conversation.IsReadOnly = true;
                    conversation.ClosedAtUtc ??= now;
                    break;
                case ChatModerationAction.DeleteConversation when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner:
                    conversation.IsReadOnly = true;
                    conversation.ClosedAtUtc ??= now;
                    conversation.DeletedAtUtc ??= now;
                    foreach (var member in conversation.Members)
                    {
                        member.RemovedAtUtc ??= now;
                        member.HiddenAtUtc ??= now;
                    }
                    break;
                case ChatModerationAction.LeaveConversation when conversation.LinkedSupportTicketId is null && conversation.IsGroup:
                    if (actorRole == GroupMemberRole.Owner)
                    {
                        throw new InvalidOperationException("Deleting the group is the owner exit path for group chats.");
                    }

                    actorMember.RemovedAtUtc ??= now;
                    actorMember.HiddenAtUtc ??= now;
                    break;
                case ChatModerationAction.HideConversation when !conversation.IsGroup:
                    actorMember.HiddenAtUtc ??= now;
                    break;
                default:
                    throw new InvalidOperationException("That action is not available for this conversation.");
            }

            return ConversationMembershipPolicy.CanViewConversation(conversation, actorAccountId)
                ? MapDetail(state, conversation, actorAccountId)
                : null;
        }, cancellationToken);
    }

    public Task<ChatMessageRecord> SendMessageAsync(Guid senderAccountId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = GetVisibleConversation(state, senderAccountId, request.ConversationId);
            if (!ConversationMembershipPolicy.CanInteractWithConversation(conversation, senderAccountId))
            {
                var senderMember = ConversationMembershipPolicy.FindMember(conversation, senderAccountId);
                if (senderMember?.RemovedAtUtc is not null)
                {
                    throw new InvalidOperationException("You were removed from this conversation.");
                }

                throw new InvalidOperationException("This conversation is closed.");
            }

            if (!conversation.IsGroup)
            {
                var otherAccountId = conversation.Members.Select(item => item.AccountId).First(id => id != senderAccountId);
                var otherAccount = state.Accounts.Single(item => item.Id == otherAccountId);
                if (AccountLabelFormatter.IsUnavailable(otherAccount))
                {
                    throw new InvalidOperationException("The number you are trying to reach is no longer in service.");
                }
            }

            var sender = state.Accounts.Single(item => item.Id == senderAccountId);
            var senderGameIdentity = request.SenderGameIdentity is not null
                ? new PersistedGameIdentity
                {
                    CharacterName = request.SenderGameIdentity.CharacterName,
                    WorldName = request.SenderGameIdentity.WorldName,
                    FullHandle = request.SenderGameIdentity.FullHandle,
                }
                : sender.LastKnownGameIdentity;

            var message = new PersistedMessage
            {
                Id = Guid.NewGuid(),
                SenderAccountId = senderAccountId,
                Body = request.Body,
                SenderGameIdentity = senderGameIdentity,
                SenderPhoneNumber = sender.PhoneNumber,
                SentAtUtc = DateTimeOffset.UtcNow,
                Kind = nameof(ChatMessageKind.User),
                RelatedCallId = null,
                RelatedCallDurationSeconds = null,
                Embeds = request.Embeds?
                    .Where(item => Uri.TryCreate(item.Url, UriKind.Absolute, out _))
                    .Select(item => new PersistedExternalEmbed
                    {
                        Id = Guid.NewGuid(),
                        Url = item.Url,
                        Kind = DetectKind(item.Url).ToString(),
                    })
                    .ToList() ?? [],
            };

            conversation.Messages.Add(message);
            if (!conversation.IsGroup)
            {
                RevealDirectConversation(conversation);
            }

            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = senderAccountId,
                ActorDisplayName = sender.DisplayName,
                EventType = "MessageSent",
                Summary = $"Message logged from username {sender.Username}, phone {sender.PhoneNumber}, game identity {senderGameIdentity?.FullHandle ?? "unknown"} in conversation {conversation.Id}.",
                CreatedAtUtc = message.SentAtUtc,
            });
            return MapMessage(state, conversation.Id, message);
        }, cancellationToken);
    }

    public Task<ConversationSummary> StartDirectConversationAsync(Guid senderAccountId, StartDirectConversationRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var target = AccountLabelFormatter.ResolveAccount(state.Accounts, request.UsernameOrPhoneNumber);
            if (AccountLabelFormatter.IsUnavailable(target))
            {
                throw new InvalidOperationException("The number you are trying to reach is no longer in service.");
            }

            var existing = state.Conversations.FirstOrDefault(item =>
                !item.IsDeleted
                && item.Kind == SystemConversationCoordinator.StandardConversationKind
                && !item.IsGroup
                && item.Members.Count == 2
                && item.Members.Any(member => member.AccountId == senderAccountId)
                && item.Members.Any(member => member.AccountId == target.Id));

            if (existing is not null)
            {
                RevealDirectConversation(existing);
                return MapSummary(state, senderAccountId, existing);
            }

            var conversation = new PersistedConversation
            {
                Id = Guid.NewGuid(),
                Name = AccountLabelFormatter.GetDisplayName(target),
                IsGroup = false,
                Kind = SystemConversationCoordinator.StandardConversationKind,
                Members =
                [
                    new PersistedConversationMember { AccountId = senderAccountId, Role = nameof(GroupMemberRole.Owner), JoinedAtUtc = DateTimeOffset.UtcNow },
                    new PersistedConversationMember { AccountId = target.Id, Role = nameof(GroupMemberRole.Member), JoinedAtUtc = DateTimeOffset.UtcNow },
                ]
            };

            state.Conversations.Add(conversation);
            return MapSummary(state, senderAccountId, conversation);
        }, cancellationToken);
    }


    public Task<bool> CanSendMessageInConversationAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = state.Conversations.SingleOrDefault(item => item.Id == conversationId && !item.IsDeleted && ConversationMembershipPolicy.CanViewConversation(item, accountId));
            if (conversation is null || !ConversationMembershipPolicy.CanInteractWithConversation(conversation, accountId))
            {
                return false;
            }

            return conversation.Kind is SystemConversationCoordinator.SupportConversationKind or SystemConversationCoordinator.StaffConversationKind;
        }, cancellationToken);
    }
    private static PersistedConversation GetVisibleConversation(PersistedAppState state, Guid accountId, Guid conversationId)
    {
        return state.Conversations.SingleOrDefault(item => item.Id == conversationId && !item.IsDeleted && ConversationMembershipPolicy.CanViewConversation(item, accountId))
            ?? throw new InvalidOperationException("Conversation unavailable.");
    }

    private static ConversationDetail MapDetail(PersistedAppState state, PersistedConversation conversation, Guid viewerAccountId)
    {
        var viewerMember = ConversationMembershipPolicy.FindMember(conversation, viewerAccountId)
            ?? throw new InvalidOperationException("Conversation unavailable.");
        var displayName = conversation.Name;
        if (!conversation.IsGroup)
        {
            var otherParticipantId = conversation.Members
                .Select(member => member.AccountId)
                .FirstOrDefault(id => id != viewerAccountId);
            var otherParticipant = state.Accounts.SingleOrDefault(item => item.Id == otherParticipantId);
            if (otherParticipant is not null)
            {
                displayName = AccountLabelFormatter.GetDisplayName(otherParticipant);
            }
        }

        return new ConversationDetail(
            conversation.Id,
            displayName,
            conversation.IsGroup,
            conversation.IsReadOnly,
            ConversationMembershipPolicy.CanInteractWithConversation(conversation, viewerAccountId),
            ConversationMembershipPolicy.IsActiveMember(viewerMember) && ParseRole(viewerMember.Role) == GroupMemberRole.Owner,
            ConversationMembershipPolicy.IsActiveMember(viewerMember),
            conversation.LinkedSupportTicketId,
            ConversationMembershipPolicy.GetActiveMembers(conversation)
                .OrderBy(member => member.JoinedAtUtc)
                .Select(member =>
                {
                    var account = state.Accounts.Single(item => item.Id == member.AccountId);
                    return new ConversationMemberRecord(member.AccountId, AccountLabelFormatter.GetDisplayName(account), account.PhoneNumber, ParseRole(member.Role), member.JoinedAtUtc);
                })
                .ToList(),
            conversation.PendingMemberRequests
                .Where(request => request.TargetAccountId != Guid.Empty && request.RequestedByAccountId != Guid.Empty)
                .Where(request => !ConversationMembershipPolicy.IsActiveMember(ConversationMembershipPolicy.FindMember(conversation, request.TargetAccountId)))
                .OrderBy(request => request.RequestedAtUtc)
                .Select(request =>
                {
                    var target = state.Accounts.SingleOrDefault(item => item.Id == request.TargetAccountId);
                    var requester = state.Accounts.SingleOrDefault(item => item.Id == request.RequestedByAccountId);
                    return new ConversationPendingMemberRequestRecord(
                        request.TargetAccountId,
                        target is null ? "Unknown contact" : AccountLabelFormatter.GetDisplayName(target),
                        target?.PhoneNumber ?? string.Empty,
                        request.RequestedByAccountId,
                        requester is null ? "Unknown member" : AccountLabelFormatter.GetDisplayName(requester),
                        request.RequestedAtUtc);
                })
                .ToList(),
            conversation.Messages
                .SelectMany(message => message.Embeds)
                .Select(embed => new ExternalMediaEmbedRecord(embed.Id, embed.Url, ParseEmbedKind(embed.Kind), embed.Url))
                .ToList());
    }

    private static ChatMessageRecord MapMessage(PersistedAppState state, Guid conversationId, PersistedMessage item)
    {
        var sender = state.Accounts.SingleOrDefault(account => account.Id == item.SenderAccountId);
        var senderName = sender is null ? $"Unknown ({item.SenderAccountId})" : AccountLabelFormatter.GetDisplayName(sender);
        var senderIdentity = item.SenderGameIdentity is null
            ? null
            : new GameIdentityRecord(item.SenderGameIdentity.CharacterName, item.SenderGameIdentity.WorldName, item.SenderGameIdentity.FullHandle);
        return new ChatMessageRecord(
            item.Id,
            conversationId,
            senderName,
            senderIdentity,
            item.Body,
            item.SentAtUtc,
            item.IsDeletedForUsers,
            item.Embeds.Select(embed => new ExternalMediaEmbedRecord(embed.Id, embed.Url, ParseEmbedKind(embed.Kind), embed.Url)).ToList(),
            ParseMessageKind(item.Kind),
            item.RelatedCallId,
            item.RelatedCallDurationSeconds);
    }

    private static bool IsMessageVisibleToViewer(PersistedAppState state, PersistedConversation conversation, PersistedMessage item, Guid viewerAccountId)
    {
        var viewerMember = ConversationMembershipPolicy.FindMember(conversation, viewerAccountId);
        if (viewerMember is null)
        {
            return false;
        }

        if (!ConversationMembershipPolicy.IsMessageVisibleToViewer(viewerMember, item))
        {
            return false;
        }

        if (conversation.IsGroup)
        {
            return true;
        }

        var sender = state.Accounts.Single(account => account.Id == item.SenderAccountId);
        var viewer = state.Accounts.Single(account => account.Id == viewerAccountId);
        return !sender.BlockedAccountIds.Contains(viewerAccountId) && !viewer.BlockedAccountIds.Contains(item.SenderAccountId);
    }

    private static ExternalEmbedKind ParseEmbedKind(string value)
    {
        return Enum.TryParse<ExternalEmbedKind>(value, out var kind) ? kind : ExternalEmbedKind.Unknown;
    }

    private static ChatMessageKind ParseMessageKind(string value)
    {
        return Enum.TryParse<ChatMessageKind>(value, out var kind) ? kind : ChatMessageKind.User;
    }

    private static string? GetMessagePreview(PersistedMessage? message)
    {
        if (message is null)
        {
            return null;
        }

        return ParseMessageKind(message.Kind) switch
        {
            ChatMessageKind.CallStarted => "Call started",
            ChatMessageKind.CallEnded => $"Call ended{(message.RelatedCallDurationSeconds is > 0 ? $" � {TimeSpan.FromSeconds(message.RelatedCallDurationSeconds.Value):m\\:ss}" : string.Empty)}",
            _ => message.Body,
        };
    }

    private static ExternalEmbedKind DetectKind(string url)
    {
        var lowered = url.ToLowerInvariant();
        if (lowered.EndsWith(".gif") || lowered.Contains("giphy") || lowered.Contains("tenor"))
        {
            return ExternalEmbedKind.Gif;
        }

        if (lowered.EndsWith(".png") || lowered.EndsWith(".jpg") || lowered.EndsWith(".jpeg") || lowered.EndsWith(".webp"))
        {
            return ExternalEmbedKind.Image;
        }

        return ExternalEmbedKind.Unknown;
    }

    private static ConversationSummary MapSummary(PersistedAppState state, Guid accountId, PersistedConversation conversation)
    {
        var viewerMember = ConversationMembershipPolicy.FindMember(conversation, accountId)
            ?? throw new InvalidOperationException("Conversation unavailable.");
        var last = conversation.Messages
            .Where(item => IsMessageVisibleToViewer(state, conversation, item, accountId))
            .OrderByDescending(item => item.SentAtUtc)
            .FirstOrDefault();
        var displayName = conversation.Name;

        if (!conversation.IsGroup)
        {
            var otherParticipant = conversation.Members.Select(item => item.AccountId).FirstOrDefault(id => id != accountId);
            var account = state.Accounts.SingleOrDefault(item => item.Id == otherParticipant);
            displayName = account is null ? conversation.Name : AccountLabelFormatter.GetDisplayName(account);
        }

        return new ConversationSummary(
            conversation.Id,
            displayName,
            conversation.IsGroup,
            GetMessagePreview(last) ?? "No messages yet.",
            last?.SentAtUtc ?? DateTimeOffset.MinValue,
            0,
            ConversationMembershipPolicy.CanInteractWithConversation(conversation, accountId),
            ConversationMembershipPolicy.IsActiveMember(viewerMember) && ParseRole(viewerMember.Role) == GroupMemberRole.Owner,
            ConversationMembershipPolicy.IsActiveMember(viewerMember));
    }

    private static GroupMemberRole ParseRole(string value)
    {
        return ConversationMembershipPolicy.ParseRole(value);
    }

    private void EnsureCanCreateOrGrowStandardGroup(PersistedAppState state, Guid ownerAccountId, int targetMemberCount, PersistedConversation? conversation = null)
    {
        if (conversation is not null && conversation.Kind != SystemConversationCoordinator.StandardConversationKind)
        {
            return;
        }

        var owner = state.Accounts.Single(item => item.Id == ownerAccountId);
        var limit = owner.IsPaidMember ? this.groupConversationPolicy.PaidMemberCap : this.groupConversationPolicy.FreeMemberCap;
        if (limit <= 0)
        {
            return;
        }

        if (targetMemberCount > limit)
        {
            var membershipLabel = owner.IsPaidMember ? "paid" : "standard";
            throw new InvalidOperationException($"This group is at the {membershipLabel} member cap of {limit}. Remove members or upgrade the host plan before adding more.");
        }
    }

    private static Guid GetConversationOwnerAccountId(PersistedConversation conversation)
    {
        var owner = ConversationMembershipPolicy.GetActiveMembers(conversation).FirstOrDefault(member => ParseRole(member.Role) == GroupMemberRole.Owner);
        return owner?.AccountId ?? conversation.Members.First().AccountId;
    }

    private static void SetMemberRole(PersistedConversation conversation, Guid accountId, GroupMemberRole role)
    {
        var member = conversation.Members.SingleOrDefault(item => item.AccountId == accountId);
        if (member is not null)
        {
            member.Role = role.ToString();
        }
    }

    private static void ReassignOwnerIfNeeded(PersistedConversation conversation)
    {
        var activeMembers = ConversationMembershipPolicy.GetActiveMembers(conversation).ToList();
        if (activeMembers.Count == 0 || activeMembers.Any(member => ParseRole(member.Role) == GroupMemberRole.Owner))
        {
            return;
        }

        var next = activeMembers
            .OrderByDescending(member => ParseRole(member.Role) == GroupMemberRole.Moderator)
            .ThenBy(member => member.JoinedAtUtc)
            .First();

        next.Role = nameof(GroupMemberRole.Owner);
    }

    private static void RevealDirectConversation(PersistedConversation conversation)
    {
        foreach (var member in conversation.Members)
        {
            member.HiddenAtUtc = null;
        }
    }
}









