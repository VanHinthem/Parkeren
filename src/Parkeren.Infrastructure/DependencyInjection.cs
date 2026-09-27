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
using Parkeren.Infrastructure.Notifications;

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
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IVisitEndTimeChanger, PostgresVisitEndTimeChanger>();
        services.AddScoped<ChangeVisitEndTimeFlow>();
        services.AddScoped<IChangeVisitEndTimeOperationalContextResolver, ChangeVisitEndTimeOperationalContextResolver>();
        services.AddScoped<IStopVisitRequestResolver, StopVisitRequestResolver>();
        services.AddScoped<IStopVisitClaimer, PostgresStopVisitClaimer>();
        services.AddScoped<IStopVisitFinalizer, StopVisitFinalizer>();
        services.AddScoped<IProviderStopStore, ProviderStopStore>();
        services.AddScoped<IProviderStopResultStore, ProviderStopResultStore>();
        services.AddScoped<StopVisitProviderReconciler>();
        services.AddScoped<StopVisitProviderExecutor>();
        services.AddScoped<StopVisitFlow>();
        services.AddScoped<IVisitStartStore, VisitStartStore>();
        services.AddScoped<IStartVisitRequestResolver, StartVisitRequestResolver>();
        services.AddScoped<IStartVisitOperationalContextResolver, StartVisitOperationalContextResolver>();
        services.AddScoped<IProviderStartStore, ProviderStartStore>();
        services.AddScoped<IProviderStartMutationGuard, ProviderStartMutationGuard>();
        services.AddScoped<IProviderStartResultStore, ProviderStartResultStore>();
        services.AddScoped<IStartVisitNotificationPublisher, StartVisitNotificationPublisher>();
        services.AddScoped<StartVisitPreparer>();
        services.AddScoped<StartVisitClaimer>();
        services.AddScoped<StartVisitFinalizer>();
        services.AddScoped<StartVisitProviderPreparer>();
        services.AddScoped<StartVisitProviderReadiness>();
        services.AddScoped<StartVisitProviderExecutor>();
        services.AddScoped<StartVisitProviderReconciler>();
        services.AddScoped<StartVisitFlow>();

        if (configuration["ParkingProvider:Type"] == "TwoParkMock")
        {
            var baseUrl = configuration["ParkingProvider:BaseUrl"]
                ?? throw new InvalidOperationException("ParkingProvider:BaseUrl is not configured.");
            services.AddHttpClient<IParkingProvider, TwoParkMockProvider>(client => client.BaseAddress = new Uri(baseUrl));
        }
        return services;
    }
}
