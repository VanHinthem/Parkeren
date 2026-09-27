using Parkeren.Application.ParkingProvider;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartReadiness(ProviderProduct Product, ProviderBalance Balance);

public sealed class StartVisitProviderReadiness(IParkingProvider provider)
{
    public async Task<ProviderStartReadiness> CheckAsync(
        TimeSpan requiredPaidDuration,
        CancellationToken cancellationToken = default)
    {
        if (requiredPaidDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requiredPaidDuration));

        var product = await provider.GetProductAsync(cancellationToken);
        var balance = await provider.GetBalanceAsync(cancellationToken);

        if (balance.RemainingPaidDuration < requiredPaidDuration)
            throw new InvalidOperationException("Parking provider balance is insufficient for the requested paid duration.");

        return new ProviderStartReadiness(product, balance);
    }
}
