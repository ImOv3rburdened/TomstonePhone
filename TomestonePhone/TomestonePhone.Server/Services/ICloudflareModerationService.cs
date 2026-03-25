namespace TomestonePhone.Server.Services;

public interface ICloudflareModerationService
{
    Task HandleCsamAlertAsync(CloudflareCsamAlert alert, CancellationToken cancellationToken = default);
}
