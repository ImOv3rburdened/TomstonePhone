namespace TomestonePhone.Server.Models;

public sealed class PersistedAppState
{
    public int SchemaVersion { get; set; }

    public long NextPhoneNumber { get; set; } = 5550102000;

    public List<PersistedAccount> Accounts { get; set; } = [];

    public List<PersistedConversation> Conversations { get; set; } = [];

    public List<PersistedCall> Calls { get; set; } = [];

    public List<PersistedActiveCallSession> ActiveCallSessions { get; set; } = [];

    public List<PersistedFriendRequest> FriendRequests { get; set; } = [];

    public List<PersistedFriendship> Friendships { get; set; } = [];

    public List<PersistedReport> Reports { get; set; } = [];

    public List<PersistedAuditLog> AuditLogs { get; set; } = [];

    public List<PersistedIpBan> IpBans { get; set; } = [];

    public List<PersistedSupportTicket> SupportTickets { get; set; } = [];

    public List<PersistedSession> Sessions { get; set; } = [];

    public PersistedServerAnnouncement? ActiveAnnouncement { get; set; }
}

