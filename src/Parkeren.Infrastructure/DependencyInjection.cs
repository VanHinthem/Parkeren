using System.Net;
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

        services.AddSingleton(configuration);
        services.AddDbContext<ParkerenDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<IVisitCapacityClaimer, PostgresVisitCapacityClaimer>();
        services.AddScoped<IVisitSchedulerWorkClaimer, PostgresVisitSchedulerWorkClaimer>();
        services.AddScoped<IVisitSchedulerWorkProcessor, VisitSchedulerWorkProcessor>();
        services.AddScoped<IVisitRecoveryService, VisitRecoveryService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IVisitEndTimeChanger, PostgresVisitEndTimeChanger>();
        services.AddScoped<IVisitEndTimeProviderAdjuster, VisitEndTimeProviderAdjuster>();
        services.AddScoped<ChangeVisitEndTimeFlow>();
        services.AddScoped<IChangeVisitEndTimeOperationalContextResolver, ChangeVisitEndTimeOperationalContextResolver>();
        services.AddScoped<IStopVisitRequestResolver, StopVisitRequestResolver>();
        services.AddScoped<IStopVisitClaimer, PostgresStopVisitClaimer>();
        services.AddScoped<IStopVisitFinalizer, StopVisitFinalizer>();
        services.AddScoped<BudgetWarningService>();
        services.AddScoped<IProviderStopStore, ProviderStopStore>();
        services.AddScoped<IProviderStopResultStore, ProviderStopResultStore>();
        services.AddScoped<StopVisitProviderReconciler>();
        services.AddScoped<StopVisitProviderExecutor>();
        services.AddScoped<StopVisitFlow>();
        services.AddScoped<IVisitStartStore, VisitStartStore>();
        services.AddScoped<IStartVisitRequestResolver, StartVisitRequestResolver>();
        services.AddScoped<IStartVisitOperationalContextResolver, StartVisitOperationalContextResolver>();
        services.AddScoped<IProviderStartStore, ProviderStartStore>();
        services.AddScoped<IProviderContinuationStartStore, ProviderContinuationStartStore>();
        services.AddScoped<IProviderContinuationStartResultStore, ProviderContinuationStartResultStore>();
        services.AddScoped<IProviderContinuationStartMutationGuard, ProviderContinuationStartMutationGuard>();
        services.AddScoped<ContinueVisitStartExecutor>();
        services.AddScoped<IProviderExtendStore, ProviderExtendStore>();
        services.AddScoped<IProviderExtendResultStore, ProviderExtendResultStore>();
        services.AddScoped<IProviderExtendMutationGuard, ProviderExtendMutationGuard>();
        services.AddScoped<ContinueVisitProviderExecutor>();
        services.AddScoped<ContinueVisitProviderReconciler>();
        services.AddScoped<IProviderStartMutationGuard, ProviderStartMutationGuard>();
        services.AddScoped<IProviderStartResultStore, ProviderStartResultStore>();
        services.AddScoped<NotificationInboxWriter>();
        services.AddScoped<PushSubscriptionService>();
        services.AddScoped<WebPushSender>();
        services.AddScoped<PushDeliveryProcessor>();
        services.AddScoped<IStartVisitNotificationPublisher, StartVisitNotificationPublisher>();
        services.AddScoped<StartVisitPreparer>();
        services.AddScoped<StartVisitClaimer>();
        services.AddScoped<StartVisitFinalizer>();
        services.AddScoped<StartVisitProviderPreparer>();
        services.AddScoped<StartVisitProviderReadiness>();
        services.AddScoped<StartVisitProviderExecutor>();
        services.AddScoped<StartVisitProviderReconciler>();
        services.AddScoped<StartVisitFlow>();

        var providerType = configuration["ParkingProvider:Type"];
        if (providerType == "TwoParkMock")
        {
            var baseUrl = configuration["ParkingProvider:BaseUrl"]
                ?? throw new InvalidOperationException("ParkingProvider:BaseUrl is not configured.");
            services.AddHttpClient<IParkingProvider, TwoParkMockProvider>(client => client.BaseAddress = new Uri(baseUrl));
        }
        else if (providerType == "TwoPark")
        {
            var baseUrl = configuration["ParkingProvider:BaseUrl"] ?? "https://mijn.2park.nl/gsmpark-app-www/json/";
            services.AddHttpClient<IParkingProvider, TwoParkProvider>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Parkeren/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            });
        }
        else if (!string.IsNullOrWhiteSpace(providerType))
        {
            throw new InvalidOperationException("ParkingProvider:Type must be configured as TwoParkMock or TwoPark.");
        }

        return services;
    }
}
