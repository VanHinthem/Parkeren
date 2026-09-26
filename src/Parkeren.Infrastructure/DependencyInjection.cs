using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Parkeren")
            ?? throw new InvalidOperationException(
                "Connection string 'Parkeren' is not configured.");

        services.AddDbContext<ParkerenDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
