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
                    // Minimal hosting registers infrastructure during Program startup.
                    // Host settings must be available before those registrations run.
                    builder.UseEnvironment("Development");
                    builder.UseSetting("ConnectionStrings:Parkeren", fixture.ConnectionString);
                    builder.UseSetting("ParkingProvider:Type", "TwoParkMock");
                    builder.UseSetting("ParkingProvider:BaseUrl", "http://localhost:5081/");
                    builder.ConfigureServices(services =>
                        services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>());
                });
            using var http = factory.CreateClient();
            var actionId = Guid.NewGuid();
            var historyUrl = "/api/admin/provider/actions";
            var syncStatusUrl = "/api/admin/provider-history/sync-status";
            var syncStartUrl = "/api/admin/provider-history/sync";
            var syncCancelUrl = $"/api/admin/provider-history/sync/{Guid.NewGuid():D}/cancel";
            var auditUrl = $"/api/admin/provider/actions/{actionId:D}/assignment-history";
            var assignmentUrl = $"/api/admin/provider/actions/{actionId:D}/assignment";

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.PostAsJsonAsync(syncStartUrl, new { ProviderProductId = "history-test-product" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.PostAsync(syncCancelUrl, null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await http.GetAsync(syncStatusUrl, ct)).StatusCode);
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
                (await http.PostAsJsonAsync(syncStartUrl, new { ProviderProductId = "history-test-product" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.PostAsync(syncCancelUrl, null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.GetAsync(syncStatusUrl, ct)).StatusCode);
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
    [Fact]
    public async Task Admin_can_reserve_one_manual_sync_and_duplicate_request_conflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var productId = $"history-http-{suffix}";
        var username = $"history-admin-{suffix}";
        const string pin = "357159";
        var admin = new User(Guid.NewGuid(), username,
            username.ToUpperInvariant(), "pending", UserRole.Admin);
        admin.ChangePinHash(new PasswordHasher<User>().HashPassword(admin, pin));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(admin);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var factory = new WebApplicationFactory<Parkeren.Api.WebPushOptions>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting("ConnectionStrings:Parkeren", fixture.ConnectionString);
                    builder.UseSetting("ParkingProvider:Type", "TwoParkMock");
                    builder.UseSetting("ParkingProvider:BaseUrl", "http://localhost:5081/");
                    builder.ConfigureServices(services =>
                        services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>());
                });
            using var http = factory.CreateClient();
            var login = await http.PostAsJsonAsync("/api/auth/login",
                new { Username = username, Pin = pin }, ct);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            var url = "/api/admin/provider-history/sync";
            var first = await http.PostAsJsonAsync(url, new { ProviderProductId = productId }, ct);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await http.PostAsJsonAsync(url, new { ProviderProductId = productId }, ct)).StatusCode);

            Guid runId;
            await using (var lookup = fixture.CreateDbContext())
                runId = await lookup.ProviderHistorySyncRuns.AsNoTracking()
                    .Where(x => x.ProviderProductId == productId)
                    .Select(x => x.Id).SingleAsync(ct);
            var cancelUrl = $"/api/admin/provider-history/sync/{runId:D}/cancel";
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(cancelUrl, null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsync(cancelUrl, null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await http.PostAsync($"/api/admin/provider-history/sync/{Guid.NewGuid():D}/cancel", null, ct)).StatusCode);

            await using var verify = fixture.CreateDbContext();
            var run = Assert.Single(await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == productId).ToListAsync(ct));
            Assert.Equal(Parkeren.Domain.ParkingProvider.ProviderHistorySyncRunStatus.Cancelled, run.Status);
            Assert.Equal(Parkeren.Domain.ParkingProvider.ProviderHistorySyncRunMode.Manual, run.Mode);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == productId).ExecuteDeleteAsync(ct);
            await cleanup.UserSessions.Where(x => x.UserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
        }
    }

}
