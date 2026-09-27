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
        return services;
    }
}
