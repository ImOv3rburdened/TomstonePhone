namespace TomestonePhone.Server.Models;

public sealed class PersistedConversation
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsGroup { get; set; }

    public string Kind { get; set; } = "Standard";

    public Guid? LinkedSupportTicketId { get; set; }

    public bool IsReadOnly { get; set; }

    public List<PersistedConversationMember> Members { get; set; } = [];

    public List<PersistedMessage> Messages { get; set; } = [];

    public bool IsDeleted { get; set; }
}
