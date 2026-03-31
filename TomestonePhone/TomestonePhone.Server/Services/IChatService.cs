using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface IChatService
{
    Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<ConversationSummary> CreateConversationAsync(Guid ownerAccountId, CreateConversationRequest request, CancellationToken cancellationToken = default);

    Task<ConversationMessagePage> GetMessagesAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default);

    Task<ChatMessageRecord> SendMessageAsync(Guid senderAccountId, SendMessageRequest request, CancellationToken cancellationToken = default);

    Task<ConversationDetail> GetConversationDetailAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default);

    Task<ConversationDetail?> ModerateConversationAsync(Guid actorAccountId, ConversationModerationRequest request, CancellationToken cancellationToken = default);

    Task<ConversationSummary> StartDirectConversationAsync(Guid senderAccountId, StartDirectConversationRequest request, CancellationToken cancellationToken = default);

    Task<bool> CanSendMessageInConversationAsync(Guid accountId, Guid conversationId, CancellationToken cancellationToken = default);
}