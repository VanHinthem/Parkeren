using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderExtendStopCoordinator(
    ParkerenDbContext dbContext,
    IProviderOperationExecutionTracker executionTracker,
    IParkingProvider provider,
    IProviderExtendResultStore resultStore) : IProviderExtendStopCoordinator
{
    public async Task<bool> WaitForInFlightExtensionsAsync(
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var inProgressOperationIds = await dbContext.ProviderOperations
                .AsNoTracking()
                .Where(x => x.VisitId == visitId &&
                            x.Type == ProviderOperationType.Extend &&
                            x.Status == ProviderOperationStatus.InProgress)
                .Select(x => x.OperationId)
                .ToListAsync(cancellationToken);

            if (inProgressOperationIds.Count == 0)
                break;

            var activeOperationIds = inProgressOperationIds
                .Where(executionTracker.IsActive)
                .ToArray();
            if (activeOperationIds.Length == 0)
            {
                var stillInProgress = await dbContext.ProviderOperations
                    .AsNoTracking()
                    .AnyAsync(x => x.VisitId == visitId &&
                                   x.Type == ProviderOperationType.Extend &&
                                   x.Status == ProviderOperationStatus.InProgress,
                        cancellationToken);
                if (stillInProgress)
                    return false;

                continue;
            }

            foreach (var operationId in activeOperationIds)
                await executionTracker.WaitUntilInactiveAsync(operationId, cancellationToken);
        }

        var unresolvedExtensions = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.VisitId == visitId &&
                        x.Type == ProviderOperationType.Extend &&
                        (x.Status == ProviderOperationStatus.Unknown ||
                         x.Status == ProviderOperationStatus.Reconciling))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var operationId in unresolvedExtensions)
        {
            if (!await ReconcileUnknownExtensionAsync(operationId, cancellationToken))
                return false;
        }

        return !await dbContext.ProviderOperations
            .AsNoTracking()
            .AnyAsync(x => x.VisitId == visitId &&
                           x.Type == ProviderOperationType.Extend &&
                           (x.Status == ProviderOperationStatus.InProgress ||
                            x.Status == ProviderOperationStatus.Unknown ||
                            x.Status == ProviderOperationStatus.Reconciling),
                cancellationToken);
    }

    private async Task<bool> ReconcileUnknownExtensionAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.Id == operationId && x.Status == ProviderOperationStatus.Unknown)
            .Select(x => new { x.Id, x.ProviderParkingActionId, x.RequestedEndAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (operation?.ProviderParkingActionId is not Guid actionId ||
            operation.RequestedEndAt is not DateTimeOffset requestedEndAt)
            return false;

        var action = await dbContext.ProviderParkingActions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == actionId, cancellationToken);
        if (action is null || string.IsNullOrWhiteSpace(action.ProviderActionId))
            return false;

        try
        {
            var actions = string.IsNullOrWhiteSpace(action.ProviderProductId)
                ? await provider.GetActionsAsync(cancellationToken)
                : await provider.GetActionsForProductAsync(action.ProviderProductId, cancellationToken);
            var matches = actions
                .Where(x => ProviderActionMatchPolicy.Matches(
                    x,
                    new ProviderActionMatchCriteria(
                        action.ProviderActionId,
                        action.ProviderProductId,
                        AllowedStatuses: ["active"])))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
                return false;

            var persistedOperation = await dbContext.ProviderOperations
                .SingleAsync(x => x.Id == operation.Id, cancellationToken);
            var persistedAction = await dbContext.ProviderParkingActions
                .SingleAsync(x => x.Id == actionId, cancellationToken);
            if (persistedOperation.Status != ProviderOperationStatus.Unknown)
                return false;

            var preparation = new ProviderExtendPreparation(
                persistedOperation,
                persistedAction,
                requestedEndAt,
                IsReplay: true);
            await resultStore.RecordStopRaceReadBackAsync(preparation, matches[0], cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}