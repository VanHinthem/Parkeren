using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkProviderException(
    string? providerCode,
    string? providerMessage,
    string message) : ProviderResponseException(providerCode, providerMessage, message)
{
    public TwoParkProviderException(string message) : this(null, null, message)
    {
    }
}