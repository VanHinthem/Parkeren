namespace Parkeren.Api;

public sealed class WebPushOptions
{
    public string? Subject { get; init; }
    public string? PublicKey { get; init; }
    public string? PrivateKey { get; init; }
}
