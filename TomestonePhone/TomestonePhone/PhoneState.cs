using TomestonePhone.Shared.Models;

namespace TomestonePhone;

public sealed class PhoneState
{
    public required PhoneProfile CurrentProfile { get; set; }

    public required List<ContactRecord> Contacts { get; set; }

    public required List<FriendshipRecord> Friends { get; set; }

    public required List<ContactRecord> BlockedContacts { get; set; }

    public required List<ConversationSummary> Conversations { get; set; }

    public required List<CallSummary> RecentCalls { get; set; }

    public required List<FriendRequestRecord> FriendRequests { get; set; }

    public required List<PhoneNotification> Notifications { get; set; }

    public required List<ReportRecord> VisibleReports { get; set; }

    public required List<AuditLogRecord> VisibleAuditLogs { get; set; }

    public required List<SupportTicketRecord> SupportTickets { get; set; }

    public ServerAnnouncementRecord? ActiveAnnouncement { get; set; }

    public ActiveCallState? ActiveCall { get; set; }

    public int UnreadConversationCount => this.Conversations.Sum(item => item.UnreadCount);

    public int MissedCallCount => this.RecentCalls.Count(item => item.Missed && !item.Acknowledged);

    public static PhoneState CreateSeeded()
    {
        return new PhoneState
        {
            CurrentProfile = new PhoneProfile(Guid.Empty, "Guest", "Tomestone", string.Empty, AccountRole.User, AccountStatus.Active, PhonePresenceStatus.Available, false, string.Empty, string.Empty, null, null, null),
            Contacts = [],
            BlockedContacts = [],
            Friends = [],
            Conversations = [],
            RecentCalls = [],
            FriendRequests = [],
            Notifications = [],
            VisibleReports = [],
            VisibleAuditLogs = [],
            SupportTickets = [],
            ActiveAnnouncement = null,
            ActiveCall = null,
        };
    }

    public void ApplySnapshot(PhoneSnapshot snapshot)
    {
        this.CurrentProfile = snapshot.Profile;
        this.Friends = snapshot.Friends.ToList();
        this.Contacts = snapshot.Contacts.ToList();
        this.BlockedContacts = snapshot.BlockedContacts.ToList();
        this.Conversations = snapshot.Conversations.ToList();
        this.RecentCalls = snapshot.RecentCalls.ToList();
        this.FriendRequests = snapshot.FriendRequests.ToList();
        this.VisibleReports = snapshot.VisibleReports.ToList();
        this.VisibleAuditLogs = snapshot.VisibleAuditLogs.ToList();
        this.SupportTickets = snapshot.SupportTickets.ToList();
        this.ActiveAnnouncement = snapshot.ActiveAnnouncement;
    }
}

