namespace Parkeren.Application.ParkingProvider;

public class ProviderResponseException(string? providerCode, string? providerMessage, string message) : InvalidOperationException(message)
{
    public string? ProviderCode { get; } = providerCode;
    public string? ProviderMessage { get; } = providerMessage;
}