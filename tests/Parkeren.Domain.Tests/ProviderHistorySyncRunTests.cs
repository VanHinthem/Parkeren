using Parkeren.Domain.ParkingProvider;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderHistorySyncRunTests
{
    [Fact]
    public void Run_tracks_pages_and_success()
    {
        var start = new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);
        var run = new ProviderHistorySyncRun(Guid.NewGuid(), " product-1 ",
            ProviderHistorySyncRunMode.Manual, start);

        run.RecordPage(10, 7, 2, 1);
        run.RecordPage(3, 1, 1, 1);
        run.Complete(start.AddMinutes(2));

        Assert.Equal("product-1", run.ProviderProductId);
        Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, run.Status);
        Assert.Equal(13, run.ReadCount);
        Assert.Equal(8, run.InsertedCount);
        Assert.Equal(3, run.RefreshedCount);
        Assert.Equal(2, run.SkippedCount);
        Assert.Throws<InvalidOperationException>(() => run.RecordPage(1, 1, 0, 0));
    }

    [Fact]
    public void Failed_run_preserves_counts_and_error()
    {
        var start = DateTimeOffset.UnixEpoch;
        var run = new ProviderHistorySyncRun(Guid.NewGuid(), "product-1",
            ProviderHistorySyncRunMode.Bootstrap, start);
        run.RecordPage(10, 9, 0, 1);
        run.Fail(start.AddMinutes(1), "Provider returned an error");

        Assert.Equal(ProviderHistorySyncRunStatus.Failed, run.Status);
        Assert.Equal(10, run.ReadCount);
        Assert.Equal("Provider returned an error", run.Error);
        Assert.Throws<InvalidOperationException>(() => run.Complete(start.AddMinutes(2)));
    }
}
