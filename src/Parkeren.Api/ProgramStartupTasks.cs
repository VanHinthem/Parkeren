using Parkeren.Application.ParkingProvider;

namespace Parkeren.Api;

internal static class ProgramStartupTasks
{
    internal static async Task SynchronizeProviderProductsAsync(IServiceProvider services, ILogger logger)
    {
        try
        {
            var productCatalog = services.GetRequiredService<IProviderProductCatalogService>();
            var sync = await productCatalog.SynchronizeAsync();
            logger.LogInformation(
                "Initial provider product synchronization completed with {ProductCount} product(s); default auto-selected: {DefaultAutoSelected}.",
                sync.Products.Count,
                sync.DefaultAutoSelected);
        }
        catch (Exception)
        {
            logger.LogWarning(
                "Initial provider product synchronization failed. Products can be synchronized manually from administration.");
        }
    }
}