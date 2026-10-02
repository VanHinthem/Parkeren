from pathlib import Path

processor = Path('src/Parkeren.Infrastructure/Visits/VisitSchedulerWorkProcessor.cs')
text = processor.read_text()
old = '''        if (nextPaid.Start > latestAction.PlannedEndAt)
        {
            if (nextPaid.Start > now)
            {
                work.Release(nextPaid.Start);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var parkingProvider = serviceProvider.GetRequiredService<IParkingProvider>();
            var remoteActions = string.IsNullOrWhiteSpace(latestAction.ProviderProductId)
                ? await parkingProvider.GetActionsAsync(cancellationToken)
                : await parkingProvider.GetActionsForProductAsync(latestAction.ProviderProductId, cancellationToken);
            var previous = ProviderActionMatchPolicy.FindUniqueMatch(
                remoteActions,
                new ProviderActionMatchCriteria(
                    latestAction.ProviderActionId,
                    latestAction.ProviderProductId));
            if (previous is null ||
                !string.Equals(previous.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                !ProviderActionMatchPolicy.TimestampsMatch(previous.End, latestAction.PlannedEndAt))
            {
                if (previous?.Status is { } status &&
                    string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase))
                    latestAction.MarkExternallyStopped(status);
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            latestAction.MarkCompleted(latestAction.PlannedEndAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, nextPaid.Start, cancellationToken);
            return;
        }'''
new = '''        if (nextPaid.Start > latestAction.PlannedEndAt)
        {
            var nextPaidPrecheckAt = ProviderCoverageSchedule.PrecheckAt(nextPaid.Start);
            if (nextPaidPrecheckAt > now)
            {
                work.Release(nextPaidPrecheckAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (latestAction.PlannedEndAt <= now)
                latestAction.MarkCompleted(latestAction.PlannedEndAt);

            await dbContext.SaveChangesAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, latestAction.PlannedEndAt, cancellationToken);
            return;
        }'''
if text.count(old) != 1:
    raise SystemExit(f'Expected one free-gap processor block, found {text.count(old)}')
text = text.replace(old, new, 1)
old = '''        if (paid.Start > now)
        {
            work.Release(paid.Start);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }'''
new = '''        if (paid.Start > now)
        {
            var paidPrecheckAt = ProviderCoverageSchedule.PrecheckAt(paid.Start);
            if (paidPrecheckAt > now)
            {
                work.Release(paidPrecheckAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }'''
if text.count(old) != 1:
    raise SystemExit(f'Expected one initial-coverage future-start block, found {text.count(old)}')
processor.write_text(text.replace(old, new, 1))

tests = Path('tests/Parkeren.IntegrationTests/Database/ParkerenDbContextTests.cs')
text = tests.read_text()
marker = '    public async Task Scheduler_skips_free_gap_and_caps_next_action_at_paid_boundary()'
method = text.find(marker)
if method < 0:
    raise SystemExit('Free-gap test method not found')
block_start = text.rfind('    [Fact]', 0, method)
block_end = text.find('\n    [Fact]', method)
if block_start < 0 or block_end < 0:
    raise SystemExit('Could not isolate free-gap test block')
block = text[block_start:block_end]
for old_value, new_value in [
    ('public async Task Scheduler_skips_free_gap_and_caps_next_action_at_paid_boundary()', 'public async Task Scheduler_preschedules_next_action_inside_free_gap_precheck_window()'),
    ('var nextPaidStart = DateTimeOffset.UtcNow.AddMinutes(-1);', 'var nextPaidStart = DateTimeOffset.UtcNow.AddMinutes(4);'),
    ('VisitSchedulerWorkType.ContinueProviderCoverage, nextPaidStart);', 'VisitSchedulerWorkType.ContinueProviderCoverage, nextPaidStart.AddMinutes(-5));'),
    ('Assert.Equal(ProviderActionState.Active, next.State);', 'Assert.Equal(ProviderActionState.Scheduled, next.State);'),
]:
    if block.count(old_value) != 1:
        raise SystemExit(f'Expected one test replacement for {old_value!r}, found {block.count(old_value)}')
    block = block.replace(old_value, new_value, 1)
tests.write_text(text[:block_start] + block + text[block_end:])

progress = Path('docs/technisch/visit-scheduler-audit/PROGRESS.md')
text = progress.read_text()
old = '- 🚧 `PostgresVisitEndTimeChanger` plant nieuw benodigde coverage na het verlengen van `DesiredEndAt` eveneens op T-5 van `nextPaid.Start`; aaneengesloten coverage blijft T-5 van de huidige provider-end.'
new = '- ✅ `PostgresVisitEndTimeChanger` plant nieuw benodigde coverage na het verlengen van `DesiredEndAt` eveneens op T-5 van `nextPaid.Start`; aaneengesloten coverage blijft T-5 van de huidige provider-end; CI groen op commit `d2fa0b25` (`fix: precheck paid window after end-time change`).\n\n### E2 — future successor over gratis periode\n\n- 🚧 `VisitSchedulerWorkProcessor` maakt binnen T-5 vóór het volgende betaalde segment de future successor direct aan; een verstreken predecessor wordt lokaal afgerond zonder afhankelijkheid van remote `active` na natuurlijke expiratie.'
if text.count(old) != 1:
    raise SystemExit(f'Expected one end-time progress line, found {text.count(old)}')
progress.write_text(text.replace(old, new, 1))
