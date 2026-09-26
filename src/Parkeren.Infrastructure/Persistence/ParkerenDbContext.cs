using Microsoft.EntityFrameworkCore;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContext(DbContextOptions<ParkerenDbContext> options)
    : DbContext(options)
{
}
