using Microsoft.AspNetCore.SignalR;

namespace TomestonePhone.Server.Hubs;

public sealed class PhoneHub : Hub
{
    public Task JoinConversation(Guid conversationId)
    {
        return this.Groups.AddToGroupAsync(this.Context.ConnectionId, conversationId.ToString());
    }

    public Task LeaveConversation(Guid conversationId)
    {
        return this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, conversationId.ToString());
    }
}
