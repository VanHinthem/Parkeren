using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Administration;
using Parkeren.Infrastructure.Authentication;
using Parkeren.Infrastructure.Persistence;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Infrastructure.Visits;

namespace Parkeren.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Parkeren")
            ?? throw new InvalidOperationException("Connection string 'Parkeren' is not configured.");

        services.AddDbContext<ParkerenDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<IVisitCapacityClaimer, PostgresVisitCapacityClaimer>();
        services.AddScoped<IVisitStartStore, VisitStartStore>();
        services.AddScoped<IProviderStartStore, ProviderStartStore>();
        services.AddScoped<IProviderStartResultStore, ProviderStartResultStore>();

        if (configuration["ParkingProvider:Type"] == "TwoParkMock")
        {
            var baseUrl = configuration["ParkingProvider:BaseUrl"]
                ?? throw new InvalidOperationException("ParkingProvider:BaseUrl is not configured.");
            services.AddHttpClient<IParkingProvider, TwoParkMockProvider>(client => client.BaseAddress = new Uri(baseUrl));
        }
        return services;
    }
}
