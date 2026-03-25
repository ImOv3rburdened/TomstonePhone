using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class CloudflareModerationService : ICloudflareModerationService
{
    private readonly IPhoneRepository repository;
    private readonly ISupportTicketService supportTicketService;

    public CloudflareModerationService(IPhoneRepository repository, ISupportTicketService supportTicketService)
    {
        this.repository = repository;
        this.supportTicketService = supportTicketService;
    }

    public async Task HandleCsamAlertAsync(CloudflareCsamAlert alert, CancellationToken cancellationToken = default)
    {
        await this.repository.WriteAsync(state =>
        {
            var account = state.Accounts.SingleOrDefault(item => item.Id == alert.AccountId);
            if (account is null)
            {
                throw new InvalidOperationException("Target account not found.");
            }

            account.Status = nameof(AccountStatus.Suspended);

            if (!string.IsNullOrWhiteSpace(alert.ReportedIpAddress))
            {
                account.KnownIpAddresses.Add(alert.ReportedIpAddress);
                if (state.IpBans.All(item => item.IpAddress != alert.ReportedIpAddress))
                {
                    state.IpBans.Add(new PersistedIpBan
                    {
                        Id = Guid.NewGuid(),
                        IpAddress = alert.ReportedIpAddress,
                        Reason = $"Cloudflare CSAM alert for {account.Username}.",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
            }

            state.AuditLogs.Add(new PersistedAuditLog
            {
                Id = Guid.NewGuid(),
                ActorAccountId = null,
                ActorDisplayName = "Cloudflare",
                EventType = "CloudflareCsamAlert",
                Summary = $"Alert for user {account.Username}, url {alert.ContentUrl}, reference {alert.SourceReference}.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            state.Reports.Add(new PersistedReport
            {
                Id = Guid.NewGuid(),
                Category = ReportCategory.Image.ToString(),
                ReporterAccountId = account.Id,
                TargetAccountId = account.Id,
                Reason = $"Cloudflare CSAM alert. {alert.Reason}",
                SuspectedCsam = true,
                Status = ReportStatus.Open.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            return 0;
        }, cancellationToken);

        await this.supportTicketService.CreateModerationTicketAsync(
            alert.AccountId,
            "Cloudflare CSAM alert",
            $"Cloudflare reported suspected CSAM. URL: {alert.ContentUrl}\nReference: {alert.SourceReference}\nReason: {alert.Reason}\nReported IP: {alert.ReportedIpAddress}",
            alert.ContentUrl,
            cancellationToken);
    }
}
