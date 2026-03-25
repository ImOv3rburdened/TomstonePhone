using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public interface IPhoneRepository
{
    Task<T> ReadAsync<T>(Func<PersistedAppState, T> action, CancellationToken cancellationToken = default);

    Task<T> WriteAsync<T>(Func<PersistedAppState, T> action, CancellationToken cancellationToken = default);
}
