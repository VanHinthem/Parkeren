using Parkeren.Application.ParkingProvider;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartReadiness(ProviderProduct Product, ProviderBalance Balance);

public sealed class StartVisitProviderReadiness(IParkingProvider provider, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan MaximumBalanceAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProviderStartReadiness> CheckAsync(TimeSpan requiredPaidDuration, CancellationToken cancellationToken = default)
    {
        if (requiredPaidDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requiredPaidDuration));

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Id) || string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Location))
            throw new InvalidOperationException("Parking provider product configuration is incomplete.");

        var balance = await provider.GetBalanceAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (balance.RetrievedAt < now - MaximumBalanceAge || balance.RetrievedAt > now + MaximumClockSkew)
            throw new InvalidOperationException("Parking provider balance is stale or has an invalid retrieval timestamp.");

        if (balance.Unit == ProviderBalanceUnit.Minute &&
            balance.RemainingBalance < (decimal)requiredPaidDuration.TotalMinutes)
            throw new InvalidOperationException("Parking provider balance is insufficient for the requested paid duration.");

        return new ProviderStartReadiness(product, balance);
    }
}
