using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminProviderHistoryHttpAuthorizationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Anonymous_and_visitor_cannot_read_or_modify_provider_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"history-visitor-{suffix}";
        const string pin = "357159";
        var visitor = new User(Guid.NewGuid(), username,
            username.ToUpperInvariant(), "pending", UserRole.Visitor);
        visitor.ChangePinHash(new PasswordHasher<User>().HashPassword(visitor, pin));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(visitor);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var factory = new WebApplicationFactory<Parkeren.Api.WebPushOptions>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, config) =>
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
                            ["ParkingProvider:Type"] = "TwoParkMock"
                        }));
                    builder.ConfigureServices(services =>
                        services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>());
                });
            using var http = factory.CreateClient();
            var actionId = Guid.NewGuid();
            var historyUrl = "/api/admin/provider/actions";
            var auditUrl = $"/api/admin/provider/actions/{actionId:D}/assignment-history";
            var assignmentUrl = $"/api/admin/provider/actions/{actionId:D}/assignment";

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.GetAsync(historyUrl, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.GetAsync(auditUrl, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.PutAsJsonAsync(assignmentUrl, new { UserId = (Guid?)null }, ct)).StatusCode);

            var login = await http.PostAsJsonAsync("/api/auth/login",
                new { Username = username, Pin = pin }, ct);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.GetAsync(historyUrl, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.GetAsync(auditUrl, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.PutAsJsonAsync(assignmentUrl, new { UserId = (Guid?)null }, ct)).StatusCode);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserSessions.Where(x => x.UserId == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == visitor.Id).ExecuteDeleteAsync(ct);
        }
    }
}
