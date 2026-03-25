using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public interface IReportService
{
    Task<IReadOnlyList<ReportRecord>> GetVisibleReportsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<ReportRecord> CreateReportAsync(Guid reporterAccountId, CreateReportRequest request, CancellationToken cancellationToken = default);

    Task<ReportReplyResult?> ReplyToReportAsync(Guid actorAccountId, ReportReplyRequest request, CancellationToken cancellationToken = default);
}
