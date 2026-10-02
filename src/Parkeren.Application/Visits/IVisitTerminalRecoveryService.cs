namespace Parkeren.Application.Visits;

public interface IVisitTerminalRecoveryService
{
    Task RecoverAsync(CancellationToken cancellationToken = default);
}
