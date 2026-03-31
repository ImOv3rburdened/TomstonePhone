namespace TomestonePhone.Shared.Models;

public sealed record AdminDashboardSnapshot(
    IReadOnlyList<AdminAccountSummary> Accounts,
    IReadOnlyList<ReportRecord> Reports,
    IReadOnlyList<AuditLogRecord> AuditLogs,
    IReadOnlyList<SupportTicketRecord> Tickets,
    ServerAnnouncementRecord? ActiveAnnouncement);