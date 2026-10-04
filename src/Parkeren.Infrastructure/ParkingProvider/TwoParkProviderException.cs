namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkProviderException(string message) : InvalidOperationException(message);