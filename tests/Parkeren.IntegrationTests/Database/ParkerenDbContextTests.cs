using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ParkerenDbContextTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Database_can_apply_migrations()
    {
        await using var context = fixture.CreateDbContext();

        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Model_matches_latest_migration_snapshot()
    {
        using var context = fixture.CreateDbContext();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var snapshot = migrationsAssembly.ModelSnapshot
            ?? throw new InvalidOperationException("No migration model snapshot found.");
        var runtimeInitializer = context.GetService<IModelRuntimeInitializer>();
        var snapshotModel = runtimeInitializer.Initialize(snapshot.Model, designTime: true);

        var operations = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            context.Model.GetRelationalModel());

        Assert.True(
            operations.Count == 0,
            "Pending model operations: " + string.Join(", ", operations.Select(x => x.GetType().Name)));
    }
}
