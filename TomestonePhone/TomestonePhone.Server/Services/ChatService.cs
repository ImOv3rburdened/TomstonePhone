using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class ChatService : IChatService
{
    private readonly IPhoneRepository repository;

    public ChatService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<ConversationSummary> CreateConversationAsync(Guid ownerAccountId, CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = new PersistedConversation
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                IsGroup = request.IsGroup,
                Kind = SystemConversationCoordinator.StandardConversationKind,
                Members = request.ParticipantIds
                    .Append(ownerAccountId)
                    .Distinct()
                    .Select(id => new PersistedConversationMember
                    {
                        AccountId = id,
                        Role = id == ownerAccountId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member),
                        JoinedAtUtc = DateTimeOffset.UtcNow,
                    })
                    .ToList(),
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
                .Where(item => !item.IsDeleted && item.Members.Any(member => member.AccountId == accountId))
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
            return MapDetail(state, conversation);
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
            var actorMember = conversation.Members.Single(member => member.AccountId == actorAccountId);
            var actorRole = ParseRole(actorMember.Role);
            var isStaffActor = SystemConversationCoordinator.IsStaffRole(actor.Role);

            if (conversation.LinkedSupportTicketId is not null && !isStaffActor)
            {
                return null;
            }

            if (conversation.Kind == SystemConversationCoordinator.StaffConversationKind && actor.Role != nameof(AccountRole.Owner))
            {
                return MapDetail(state, conversation);
            }

            if (conversation.LinkedSupportTicketId is null && actorRole is GroupMemberRole.Member)
            {
                return null;
            }

            switch (request.Action)
            {
                case ChatModerationAction.AddMember when request.TargetAccountId is { } addId:
                    if (conversation.Members.All(member => member.AccountId != addId))
                    {
                        conversation.Members.Add(new PersistedConversationMember
                        {
                            AccountId = addId,
                            Role = nameof(GroupMemberRole.Member),
                            JoinedAtUtc = DateTimeOffset.UtcNow,
                        });
                    }
                    break;
                case ChatModerationAction.RemoveMember when request.TargetAccountId is { } removeId:
                    conversation.Members.RemoveAll(member => member.AccountId == removeId);
                    ReassignOwnerIfNeeded(conversation);
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
                case ChatModerationAction.DeleteConversation when conversation.LinkedSupportTicketId is null && actorRole == GroupMemberRole.Owner:
                    conversation.IsDeleted = true;
                    break;
            }

            return conversation.IsDeleted ? null : MapDetail(state, conversation);
        }, cancellationToken);
    }

    public Task<ChatMessageRecord> SendMessageAsync(Guid senderAccountId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            SystemConversationCoordinator.EnsureStaffConversation(state);
            var conversation = GetVisibleConversation(state, senderAccountId, request.ConversationId);
            if (conversation.IsReadOnly)
            {
                throw new InvalidOperationException("This conversation is closed.");
            }

            if (!conversation.IsGroup)
            {
                var otherAccountId = conversation.Members.Select(item => item.AccountId).First(id => id != senderAccountId);
                var otherAccount = state.Accounts.Single(item => item.Id == otherAccountId);
                if (otherAccount.Status == nameof(AccountStatus.Banned))
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
            var target = state.Accounts.Single(item =>
                item.Username.Equals(request.UsernameOrPhoneNumber, StringComparison.OrdinalIgnoreCase)
                || item.PhoneNumber == request.UsernameOrPhoneNumber);

            var existing = state.Conversations.FirstOrDefault(item =>
                !item.IsDeleted
                && item.Kind == SystemConversationCoordinator.StandardConversationKind
                && !item.IsGroup
                && item.Members.Count == 2
                && item.Members.Any(member => member.AccountId == senderAccountId)
                && item.Members.Any(member => member.AccountId == target.Id));

            if (existing is not null)
            {
                return MapSummary(state, senderAccountId, existing);
            }

            var conversation = new PersistedConversation
            {
                Id = Guid.NewGuid(),
                Name = target.DisplayName,
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

    private static PersistedConversation GetVisibleConversation(PersistedAppState state, Guid accountId, Guid conversationId)
    {
        return state.Conversations.Single(item => item.Id == conversationId && !item.IsDeleted && item.Members.Any(member => member.AccountId == accountId));
    }

    private static ConversationDetail MapDetail(PersistedAppState state, PersistedConversation conversation)
    {
        return new ConversationDetail(
            conversation.Id,
            conversation.Name,
            conversation.IsGroup,
            conversation.IsReadOnly,
            conversation.LinkedSupportTicketId,
            conversation.Members
                .OrderBy(member => member.JoinedAtUtc)
                .Select(member =>
                {
                    var account = state.Accounts.Single(item => item.Id == member.AccountId);
                    return new ConversationMemberRecord(member.AccountId, account.DisplayName, ParseRole(member.Role), member.JoinedAtUtc);
                })
                .ToList(),
            conversation.Messages
                .SelectMany(message => message.Embeds)
                .Select(embed => new ExternalMediaEmbedRecord(embed.Id, embed.Url, ParseEmbedKind(embed.Kind), embed.Url))
                .ToList());
    }

    private static ChatMessageRecord MapMessage(PersistedAppState state, Guid conversationId, PersistedMessage item)
    {
        return new ChatMessageRecord(
            item.Id,
            conversationId,
            state.Accounts.Single(account => account.Id == item.SenderAccountId).DisplayName,
            null,
            item.Body,
            item.SentAtUtc,
            item.IsDeletedForUsers,
            item.Embeds.Select(embed => new ExternalMediaEmbedRecord(embed.Id, embed.Url, ParseEmbedKind(embed.Kind), embed.Url)).ToList());
    }

    private static bool IsMessageVisibleToViewer(PersistedAppState state, PersistedConversation conversation, PersistedMessage item, Guid viewerAccountId)
    {
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
        var last = conversation.Messages.OrderByDescending(item => item.SentAtUtc).FirstOrDefault();
        var displayName = conversation.Name;

        if (!conversation.IsGroup)
        {
            var otherParticipant = conversation.Members.Select(item => item.AccountId).FirstOrDefault(id => id != accountId);
            var account = state.Accounts.SingleOrDefault(item => item.Id == otherParticipant);
            displayName = account?.DisplayName ?? conversation.Name;
        }

        return new ConversationSummary(
            conversation.Id,
            displayName,
            conversation.IsGroup,
            last?.Body ?? "No messages yet.",
            last?.SentAtUtc ?? DateTimeOffset.MinValue,
            0);
    }

    private static GroupMemberRole ParseRole(string value)
    {
        return Enum.TryParse<GroupMemberRole>(value, out var role) ? role : GroupMemberRole.Member;
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
        if (conversation.Members.Count == 0 || conversation.Members.Any(member => ParseRole(member.Role) == GroupMemberRole.Owner))
        {
            return;
        }

        var next = conversation.Members
            .OrderByDescending(member => ParseRole(member.Role) == GroupMemberRole.Moderator)
            .ThenBy(member => member.JoinedAtUtc)
            .First();

        next.Role = nameof(GroupMemberRole.Owner);
    }
}

