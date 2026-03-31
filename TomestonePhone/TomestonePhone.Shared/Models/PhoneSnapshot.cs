namespace TomestonePhone.Shared.Models;

public sealed record PhoneSnapshot(
    PhoneProfile Profile,
    IReadOnlyList<FriendshipRecord> Friends,
    IReadOnlyList<ContactRecord> Contacts,
    IReadOnlyList<ContactRecord> BlockedContacts,
    IReadOnlyList<ConversationSummary> Conversations,
    IReadOnlyList<CallSummary> RecentCalls,
    IReadOnlyList<FriendRequestRecord> FriendRequests,
    IReadOnlyList<ReportRecord> VisibleReports,
    IReadOnlyList<AuditLogRecord> VisibleAuditLogs,
    IReadOnlyList<SupportTicketRecord> SupportTickets,
    ServerAnnouncementRecord? ActiveAnnouncement);