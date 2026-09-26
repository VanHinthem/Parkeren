using Microsoft.EntityFrameworkCore;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

public sealed class ParkerenDbContextTests
{
    [Fact]
    public void Context_can_build_model()
    {
        var options = new DbContextOptionsBuilder<ParkerenDbContext>()
            .UseNpgsql("Host=localhost;Database=parkeren_test;Username=test;Password=test")
            .Options;

        using var context = new ParkerenDbContext(options);

        Assert.NotNull(context.Model);
    }
}
