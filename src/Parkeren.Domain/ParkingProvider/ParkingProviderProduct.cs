namespace Parkeren.Domain.ParkingProvider;

public sealed class ParkingProviderProduct
{
    private ParkingProviderProduct() { }

    public ParkingProviderProduct(
        Guid id,
        string providerProductId,
        string name,
        string? categoryId,
        string? categoryName,
        string location,
        DateTimeOffset seenAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(providerProductId)) throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Provider location is required.", nameof(location));

        Id = id;
        ProviderProductId = providerProductId.Trim();
        Name = name.Trim();
        CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim();
        Location = location.Trim();
        IsAvailable = true;
        FirstSeenAt = seenAt;
        LastSeenAt = seenAt;
    }

    public Guid Id { get; private set; }
    public string ProviderProductId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? CategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public string Location { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public decimal? LastSuccessfulBalance { get; private set; }
    public string? LastSuccessfulBalanceUnit { get; private set; }
    public DateTimeOffset? LastSuccessfulBalanceAt { get; private set; }

    public void Refresh(
        string name,
        string? categoryId,
        string? categoryName,
        string location,
        DateTimeOffset seenAt)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Provider location is required.", nameof(location));
        if (seenAt < FirstSeenAt) throw new ArgumentOutOfRangeException(nameof(seenAt));

        Name = name.Trim();
        CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim();
        Location = location.Trim();
        IsAvailable = true;
        LastSeenAt = seenAt;
    }

    public void MarkUnavailable() => IsAvailable = false;

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
