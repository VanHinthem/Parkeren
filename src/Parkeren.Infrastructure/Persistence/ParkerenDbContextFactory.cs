using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContextFactory : IDesignTimeDbContextFactory<ParkerenDbContext>
{
    public ParkerenDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ParkerenDbContext>()
            .UseNpgsql("Host=localhost;Database=parkeren_design;Username=postgres;Password=postgres")
            .Options;

        return new ParkerenDbContext(options);
    }
}
