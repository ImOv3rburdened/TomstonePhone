using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class ReportService : IReportService
{
    private readonly IPhoneRepository repository;

    public ReportService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<ReportRecord> CreateReportAsync(Guid reporterAccountId, CreateReportRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            var reporter = state.Accounts.Single(item => item.Id == reporterAccountId);
            var record = new PersistedReport
            {
                Id = Guid.NewGuid(),
                Category = request.Category.ToString(),
                ReporterAccountId = reporterAccountId,
                TargetAccountId = request.TargetAccountId,
                TargetConversationId = request.TargetConversationId,
                TargetMessageId = request.TargetMessageId,
                TargetImageId = request.TargetImageId,
                Reason = request.Reason,
                SuspectedCsam = request.SuspectedCsam,
                Status = ReportStatus.Open.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            state.Reports.Add(record);
            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = reporterAccountId,
                ActorDisplayName = reporter.DisplayName,
                EventType = "ReportCreated",
                Summary = $"Report {record.Id} created for {record.Category}.",
                CreatedAtUtc = record.CreatedAtUtc,
            });

            if (record.SuspectedCsam && request.TargetAccountId is { } targetAccountId)
            {
                var target = state.Accounts.SingleOrDefault(item => item.Id == targetAccountId);
                if (target is not null)
                {
                    target.Status = AccountStatus.Banned.ToString();
                    foreach (var ip in target.KnownIpAddresses)
                    {
                        if (state.IpBans.All(item => item.IpAddress != ip))
                        {
                            state.IpBans.Add(new PersistedIpBan
                            {
                                Id = Guid.NewGuid(),
                                IpAddress = ip,
                                Reason = $"Automatic ban due to suspected CSAM report {record.Id}.",
                                CreatedAtUtc = DateTimeOffset.UtcNow,
                            });
                        }
                    }

                    state.AuditLogs.Add(new PersistedAuditLog
                    {
                        Id = Guid.NewGuid(),
                        ActorAccountId = reporterAccountId,
                        ActorDisplayName = reporter.DisplayName,
                        EventType = "AutomaticPermanentBan",
                        Summary = $"Account {target.Username} permanently banned after suspected CSAM report {record.Id}.",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
            }

            return Map(state, record);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ReportRecord>> GetVisibleReportsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<ReportRecord>>(state =>
        {
            var account = state.Accounts.Single(item => item.Id == accountId);
            var isStaff = account.Role is nameof(AccountRole.Owner) or nameof(AccountRole.Admin) or nameof(AccountRole.Moderator);

            return state.Reports
                .Where(item => isStaff || item.ReporterAccountId == accountId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => Map(state, item))
                .ToList();
        }, cancellationToken);
    }

    public Task<ReportReplyResult?> ReplyToReportAsync(Guid actorAccountId, ReportReplyRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<ReportReplyResult?>(state =>
        {
            var actor = state.Accounts.Single(item => item.Id == actorAccountId);
            if (actor.Role is not nameof(AccountRole.Owner) and not nameof(AccountRole.Admin) and not nameof(AccountRole.Moderator))
            {
                return null;
            }

            var report = state.Reports.SingleOrDefault(item => item.Id == request.ReportId);
            if (report is null)
            {
                return null;
            }

            Guid? conversationId = null;
            if (request.OpenStaffChat)
            {
                var staffIds = state.Accounts
                    .Where(item => item.Role is nameof(AccountRole.Owner) or nameof(AccountRole.Admin) or nameof(AccountRole.Moderator))
                    .Select(item => item.Id)
                    .ToList();

                var members = staffIds
                    .Append(report.ReporterAccountId)
                    .Distinct()
                    .Select(id => new PersistedConversationMember
                    {
                        AccountId = id,
                        Role = id == actorAccountId ? nameof(GroupMemberRole.Owner) : nameof(GroupMemberRole.Member),
                        JoinedAtUtc = DateTimeOffset.UtcNow,
                    })
                    .ToList();

                var conversation = new PersistedConversation
                {
                    Id = Guid.NewGuid(),
                    Name = $"Report Case {report.Id.ToString()[..8]}",
                    IsGroup = true,
                    Members = members,
                    Messages =
                    [
                        new PersistedMessage
                        {
                            Id = Guid.NewGuid(),
                            SenderAccountId = actorAccountId,
                            Body = $"Case opened for report {report.Id}. Target account: {report.TargetAccountId?.ToString() ?? "n/a"}. Reason: {report.Reason}\n\n{request.ReplyBody}",
                            SentAtUtc = DateTimeOffset.UtcNow,
                        }
                    ]
                };

                state.Conversations.Add(conversation);
                conversationId = conversation.Id;
            }

            report.Status = ReportStatus.Escalated.ToString();
            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = actorAccountId,
                ActorDisplayName = actor.DisplayName,
                EventType = "ReportReply",
                Summary = $"Report {report.Id} replied to. Case thread: {conversationId?.ToString() ?? "none"}.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            return new ReportReplyResult(report.Id, conversationId, "Escalated");
        }, cancellationToken);
    }

    private static ReportRecord Map(PersistedAppState state, PersistedReport report)
    {
        var reporter = state.Accounts.SingleOrDefault(item => item.Id == report.ReporterAccountId);
        return new ReportRecord(
            report.Id,
            Enum.TryParse<ReportCategory>(report.Category, out var category) ? category : ReportCategory.Message,
            report.ReporterAccountId,
            reporter?.DisplayName ?? "Unknown",
            report.TargetAccountId,
            report.TargetConversationId,
            report.TargetMessageId,
            report.TargetImageId,
            report.Reason,
            report.SuspectedCsam,
            Enum.TryParse<ReportStatus>(report.Status, out var status) ? status : ReportStatus.Open,
            report.CreatedAtUtc);
    }
}
