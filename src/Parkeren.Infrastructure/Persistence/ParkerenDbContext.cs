using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Administration;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContext(DbContextOptions<ParkerenDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();
    public DbSet<DefaultParkingPolicy> DefaultParkingPolicies => Set<DefaultParkingPolicy>();
    public DbSet<ParkingSystemSettings> ParkingSystemSettings => Set<ParkingSystemSettings>();
    public DbSet<UserPolicyOverride> UserPolicyOverrides => Set<UserPolicyOverride>();
    public DbSet<ParkingRuleSet> ParkingRuleSets => Set<ParkingRuleSet>();
    public DbSet<ParkingBudgetPeriod> ParkingBudgetPeriods => Set<ParkingBudgetPeriod>();
    public DbSet<ParkingTariff> ParkingTariffs => Set<ParkingTariff>();
    public DbSet<PaidWindow> PaidWindows => Set<PaidWindow>();
    public DbSet<ParkingCalendarException> ParkingCalendarExceptions => Set<ParkingCalendarException>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<ProviderParkingAction> ProviderParkingActions => Set<ProviderParkingAction>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<VisitEndTimeChange> VisitEndTimeChanges => Set<VisitEndTimeChange>();
    public DbSet<VisitSchedulerWork> VisitSchedulerWork => Set<VisitSchedulerWork>();
    public DbSet<VisitSchedulerAuditEvent> VisitSchedulerAuditEvents => Set<VisitSchedulerAuditEvent>();
    public DbSet<ParkingBudgetWarningState> ParkingBudgetWarningStates => Set<ParkingBudgetWarningState>();
    public DbSet<ParkingProviderProduct> ParkingProviderProducts => Set<ParkingProviderProduct>();
    public DbSet<ProviderDiscrepancy> ProviderDiscrepancies => Set<ProviderDiscrepancy>();
    public DbSet<AdminAuditEvent> AdminAuditEvents => Set<AdminAuditEvent>();
    private int nextAuditEventOrder = 1;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            CaptureVisitSchedulerAuditEvents();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch
        {
            DetachAuditEventsAddedByFailedSave();
            throw;
        }
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return SaveChangesWithAuditAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SaveChangesWithAuditAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        try
        {
            CaptureVisitSchedulerAuditEvents();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch
        {
            DetachAuditEventsAddedByFailedSave();
            throw;
        }
    }

    internal void RecordProviderOperationReconciliationStarted(ProviderOperation operation)
    {
        if (operation.VisitId is not Guid visitId)
            throw new InvalidOperationException("Provider reconciliation requires a Visit.");

        var version = Entry(operation).Property(x => x.Version).OriginalValue;
        var eventType = "provider_operation.reconciliation_started";
        AddAuditEvent(
            visitId,
            DateTimeOffset.UtcNow,
            "provider_operation",
            operation.Id,
            eventType,
            $"attempt:{operation.OperationId}:{operation.AttemptCount}",
            operation.AttemptCount,
            "provider_operation_reconciliation_started",
            new
            {
                type = operation.Type.ToString(),
                previousStatus = ProviderOperationStatus.Unknown.ToString(),
                status = ProviderOperationStatus.Reconciling.ToString(),
                operation.AttemptCount,
                operation.LastErrorCode,
                operation.ProviderParkingActionId,
                operation.RequestedEndAt
            },
            $"provider-operation:{operation.Id}:version:{version}:{eventType}");
    }

    private void DetachAuditEventsAddedByFailedSave()
    {
        foreach (var entry in ChangeTracker.Entries<VisitSchedulerAuditEvent>().ToArray())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }
    }

    private void CaptureVisitSchedulerAuditEvents()
    {
        ChangeTracker.DetectChanges();
        var occurredAt = DateTimeOffset.UtcNow;
        CaptureVisitAuditEvents(occurredAt);
        CaptureProviderOperationAuditEvents(occurredAt);
        CaptureProviderActionAuditEvents(occurredAt);
        CaptureSchedulerWorkAuditEvents(occurredAt);
    }

    private void CaptureVisitAuditEvents(DateTimeOffset occurredAt)
    {
        foreach (var entry in ChangeTracker.Entries<Visit>().ToArray())
        {
            var visit = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                AddAuditEvent(
                    visit.Id, occurredAt, "visit", visit.Id, "visit.created", $"visit:{visit.Id}", null,
                    "visit_created", new
                    {
                        status = visit.Status.ToString(),
                        health = visit.Health.ToString(),
                        visit.StartAt,
                        visit.DesiredEndAt
                    },
                    $"visit:{visit.Id}:created");
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var previousStatus = entry.Property(x => x.Status).OriginalValue;
            var previousHealth = entry.Property(x => x.Health).OriginalValue;
            var statusChanged = previousStatus != visit.Status;
            var healthChanged = previousHealth != visit.Health;
            if (!statusChanged && !healthChanged)
                continue;

            var eventType = statusChanged ? "visit.status_changed" : "visit.health_changed";
            var version = entry.Property(x => x.Version).OriginalValue;
            AddAuditEvent(
                visit.Id, occurredAt, "visit", visit.Id, eventType, $"visit:{visit.Id}", null,
                statusChanged ? "visit_status_changed" : "visit_health_changed",
                new
                {
                    previousStatus = previousStatus.ToString(),
                    status = visit.Status.ToString(),
                    previousHealth = previousHealth.ToString(),
                    health = visit.Health.ToString(),
                    endReason = visit.EndReason?.ToString(),
                    visit.ActualEndAt
                },
                $"visit:{visit.Id}:version:{version}:{eventType}");
        }
    }

    private void CaptureProviderOperationAuditEvents(DateTimeOffset occurredAt)
    {
        foreach (var entry in ChangeTracker.Entries<ProviderOperation>().ToArray())
        {
            var operation = entry.Entity;
            if (operation.VisitId is not Guid visitId)
                continue;

            if (entry.State == EntityState.Added)
            {
                AddAuditEvent(
                    visitId, occurredAt, "provider_operation", operation.Id, "provider_operation.created",
                    $"attempt:{operation.OperationId}:{operation.AttemptCount}", operation.AttemptCount,
                    "provider_operation_created", new
                    {
                        type = operation.Type.ToString(),
                        status = operation.Status.ToString(),
                        operation.AttemptCount,
                        operation.ProviderParkingActionId,
                        operation.RequestedEndAt
                    },
                    $"provider-operation:{operation.Id}:created");
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var previousStatus = entry.Property(x => x.Status).OriginalValue;
            var previousAttemptCount = entry.Property(x => x.AttemptCount).OriginalValue;
            var previousErrorCode = entry.Property(x => x.LastErrorCode).OriginalValue;
            var statusChanged = previousStatus != operation.Status;
            var attemptChanged = previousAttemptCount != operation.AttemptCount;
            var errorChanged = previousErrorCode != operation.LastErrorCode;
            if (!statusChanged && !attemptChanged && !errorChanged)
                continue;

            var version = entry.Property(x => x.Version).OriginalValue;
            var groupKey = $"attempt:{operation.OperationId}:{operation.AttemptCount}";
            if (attemptChanged)
            {
                const string attemptStartedEvent = "provider_operation.attempt_started";
                AddAuditEvent(
                    visitId, occurredAt, "provider_operation", operation.Id, attemptStartedEvent,
                    groupKey, operation.AttemptCount, "provider_operation_attempt_started", new
                    {
                        type = operation.Type.ToString(),
                        previousStatus = previousStatus.ToString(),
                        status = ProviderOperationStatus.InProgress.ToString(),
                        previousAttemptCount,
                        attemptCount = operation.AttemptCount,
                        previousErrorCode,
                        errorCode = operation.LastErrorCode,
                        operation.ProviderParkingActionId,
                        operation.RequestedEndAt
                    },
                    $"provider-operation:{operation.Id}:version:{version}:{attemptStartedEvent}");
            }

            if ((statusChanged && operation.Status != ProviderOperationStatus.InProgress) ||
                (!attemptChanged && errorChanged))
            {
                var resultEvent = operation.Status switch
                {
                    ProviderOperationStatus.Unknown => "provider_operation.outcome_unknown",
                    ProviderOperationStatus.Reconciling => "provider_operation.reconciliation_started",
                    ProviderOperationStatus.Succeeded => "provider_operation.succeeded",
                    ProviderOperationStatus.Failed => "provider_operation.failed",
                    ProviderOperationStatus.Pending => "provider_operation.retry_ready",
                    _ => "provider_operation.state_changed"
                };
                AddAuditEvent(
                    visitId, occurredAt, "provider_operation", operation.Id, resultEvent,
                    groupKey, operation.AttemptCount, resultEvent.Replace('.', '_'), new
                    {
                        type = operation.Type.ToString(),
                        previousStatus = attemptChanged
                            ? ProviderOperationStatus.InProgress.ToString()
                            : previousStatus.ToString(),
                        status = operation.Status.ToString(),
                        previousAttemptCount = attemptChanged ? operation.AttemptCount : previousAttemptCount,
                        attemptCount = operation.AttemptCount,
                        previousErrorCode,
                        errorCode = operation.LastErrorCode,
                        operation.ProviderParkingActionId,
                        operation.RequestedEndAt,
                        operation.CompletedAt
                    },
                    $"provider-operation:{operation.Id}:version:{version}:{resultEvent}");
            }
        }
    }

    private void CaptureProviderActionAuditEvents(DateTimeOffset occurredAt)
    {
        foreach (var entry in ChangeTracker.Entries<ProviderParkingAction>().ToArray())
        {
            var action = entry.Entity;
            if (action.VisitId is not Guid visitId)
                continue;

            if (entry.State == EntityState.Added)
            {
                AddAuditEvent(
                    visitId, occurredAt, "provider_action", action.Id, "provider_action.created",
                    $"action:{action.Id}", null, "provider_action_created", new
                    {
                        state = action.State.ToString(),
                        health = action.Health.ToString(),
                        action.PlannedStartAt,
                        action.PlannedEndAt,
                        action.ProviderProductId
                    },
                    $"provider-action:{action.Id}:created");
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var previousState = entry.Property(x => x.State).OriginalValue;
            var previousHealth = entry.Property(x => x.Health).OriginalValue;
            var previousHistoryStatus = entry.Property(x => x.HistoryStatus).OriginalValue;
            var stateChanged = previousState != action.State;
            var healthChanged = previousHealth != action.Health;
            var historyChanged = previousHistoryStatus != action.HistoryStatus;
            var timingChanged = entry.Property(x => x.PlannedStartAt).IsModified ||
                entry.Property(x => x.PlannedEndAt).IsModified ||
                entry.Property(x => x.ActualStartAt).IsModified ||
                entry.Property(x => x.ActualEndAt).IsModified;
            if (!stateChanged && !healthChanged && !historyChanged && !timingChanged)
                continue;

            var eventType = stateChanged
                ? "provider_action.state_changed"
                : healthChanged
                    ? "provider_action.health_changed"
                    : historyChanged
                        ? "provider_action.history_changed"
                        : "provider_action.timing_changed";
            var version = entry.Property(x => x.Version).OriginalValue;
            AddAuditEvent(
                visitId, occurredAt, "provider_action", action.Id, eventType, $"action:{action.Id}", null,
                eventType.Replace('.', '_'), new
                {
                    previousState = previousState.ToString(),
                    state = action.State.ToString(),
                    previousHealth = previousHealth.ToString(),
                    health = action.Health.ToString(),
                    previousHistoryStatus = previousHistoryStatus.ToString(),
                    historyStatus = action.HistoryStatus.ToString(),
                    action.PlannedStartAt,
                    action.PlannedEndAt,
                    action.ActualStartAt,
                    action.ActualEndAt,
                    action.ProviderStatus,
                    action.ProviderCostAmount
                },
                $"provider-action:{action.Id}:version:{version}:{eventType}");
        }
    }

    private void CaptureSchedulerWorkAuditEvents(DateTimeOffset occurredAt)
    {
        foreach (var entry in ChangeTracker.Entries<VisitSchedulerWork>().ToArray())
        {
            var work = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                AddSchedulerWorkAuditEvent(
                    work,
                    occurredAt,
                    "scheduler_work.created",
                    "work_created",
                    null,
                    work.Status,
                    null,
                    work.DueAt,
                    $"scheduler-work:{work.Id}:created");
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var oldStatus = entry.Property(x => x.Status).OriginalValue;
            var oldDueAt = entry.Property(x => x.DueAt).OriginalValue;
            var statusChanged = oldStatus != work.Status;
            var dueAtChanged = oldDueAt != work.DueAt;
            if (!statusChanged && !dueAtChanged)
                continue;

            var (eventType, defaultReasonCode) = statusChanged
                ? work.Status switch
                {
                    VisitSchedulerWorkStatus.Claimed => ("scheduler_work.claimed", "due_work_claimed"),
                    VisitSchedulerWorkStatus.Pending => ("scheduler_work.released", "work_rescheduled"),
                    VisitSchedulerWorkStatus.Completed => ("scheduler_work.completed", "work_completed"),
                    VisitSchedulerWorkStatus.Cancelled => ("scheduler_work.cancelled", "work_cancelled"),
                    _ => ("scheduler_work.changed", "work_state_changed")
                }
                : ("scheduler_work.deferred", "due_time_changed");
            var version = entry.Property(x => x.Version).OriginalValue;
            AddSchedulerWorkAuditEvent(
                work,
                occurredAt,
                eventType,
                work.AuditReasonCode ?? defaultReasonCode,
                oldStatus,
                work.Status,
                oldDueAt,
                work.DueAt,
                $"scheduler-work:{work.Id}:version:{version}:{eventType}");
        }
    }

    private void AddSchedulerWorkAuditEvent(
        VisitSchedulerWork work,
        DateTimeOffset occurredAt,
        string eventType,
        string reasonCode,
        VisitSchedulerWorkStatus? previousStatus,
        VisitSchedulerWorkStatus currentStatus,
        DateTimeOffset? previousDueAt,
        DateTimeOffset currentDueAt,
        string eventKey)
    {
        AddAuditEvent(
            work.VisitId, occurredAt, "scheduler_work", work.Id, eventType,
            $"attempt:{work.Id}:{work.AttemptCount}", work.AttemptCount,
            reasonCode, new
            {
                workType = work.Type.ToString(),
                previousStatus = previousStatus?.ToString(),
                status = currentStatus.ToString(),
                previousDueAt,
                dueAt = currentDueAt,
                attemptCount = work.AttemptCount,
                endReason = work.EndReason?.ToString()
            },
            eventKey);
    }

    private void AddAuditEvent(
        Guid visitId,
        DateTimeOffset occurredAt,
        string sourceType,
        Guid sourceId,
        string eventType,
        string groupKey,
        int? attemptNumber,
        string reasonCode,
        object details,
        string eventKey)
    {
        VisitSchedulerAuditEvents.Add(new VisitSchedulerAuditEvent(
            Guid.NewGuid(),
            visitId,
            occurredAt,
            nextAuditEventOrder++,
            sourceType,
            sourceId,
            eventType,
            groupKey,
            attemptNumber,
            reasonCode,
            JsonSerializer.Serialize(details),
            eventKey));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.IsActive);
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PinHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.IsActive);
            entity.Property(x => x.LicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(x => x.NormalizedLicensePlate).HasMaxLength(16).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
            entity.HasIndex(x => x.NormalizedLicensePlate).IsUnique();
        });

        modelBuilder.Entity<UserVehicle>(entity =>
        {
            entity.ToTable("user_vehicles");
            entity.HasKey(x => new { x.UserId, x.VehicleId });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ParkingSystemSettings>(entity =>
        {
            entity.ToTable("parking_system_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxConcurrentVisits).IsRequired();
            entity.Property(x => x.LongVisitWarningAfter);
            entity.Property(x => x.NotifyAdminOnLongVisit).IsRequired();
            entity.Property(x => x.LongVisitReminderInterval);
            entity.Property(x => x.BudgetWarningThresholdPercentages).HasColumnType("integer[]").IsRequired();
        });
        modelBuilder.Entity<DefaultParkingPolicy>(entity =>
        {
            entity.ToTable("default_parking_policy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxPaidParkingDuration);
        });
        modelBuilder.Entity<ParkingRuleSet>(entity =>
        {
            entity.ToTable("parking_rule_sets"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Continuation).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
            entity.HasMany(x => x.PaidWindows).WithOne().HasForeignKey(x => x.ParkingRuleSetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.CalendarExceptions).WithOne().HasForeignKey(x => x.ParkingRuleSetId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ParkingCalendarException>(entity =>
        {
            entity.ToTable("parking_calendar_exceptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Date).HasColumnType("date");
            entity.HasIndex(x => new { x.ParkingRuleSetId, x.Date }).IsUnique();
        });
        modelBuilder.Entity<PaidWindow>(entity =>
        {
            entity.ToTable("paid_windows"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Day).HasConversion<int>();
            entity.Property(x => x.Start).HasColumnType("time without time zone");
            entity.Property(x => x.End).HasColumnType("time without time zone");
            entity.HasIndex(x => new { x.ParkingRuleSetId, x.Day, x.Start, x.End }).IsUnique();
        });
        modelBuilder.Entity<UserPolicyOverride>(entity =>
        {
            entity.ToTable("user_policy_overrides");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.MaxPaidParkingDurationMode)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(x => x.MaxVisitElapsedDurationMode)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.HasOne<User>().WithOne().HasForeignKey<UserPolicyOverride>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ParkingTariff>(entity =>
        {
            entity.ToTable("parking_tariffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Rate).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasConversion<int>();
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
        });

        modelBuilder.Entity<ParkingBudgetPeriod>(entity =>
        {
            entity.ToTable("parking_budget_periods");
            entity.HasKey(x => x.Id);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
        });

        modelBuilder.Entity<ParkingBudgetWarningState>(entity =>
        {
            entity.ToTable("parking_budget_warning_states");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ParkingBudgetPeriodId, x.ThresholdPercentage }).IsUnique();
            entity.HasOne<ParkingBudgetPeriod>().WithMany().HasForeignKey(x => x.ParkingBudgetPeriodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParkingProviderProduct>(entity =>
        {
            entity.ToTable("parking_provider_products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderProductId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CategoryId).HasMaxLength(100);
            entity.Property(x => x.CategoryName).HasMaxLength(200);
            entity.Property(x => x.Location).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastSuccessfulBalance).HasPrecision(18, 4);
            entity.Property(x => x.LastSuccessfulBalanceUnit).HasMaxLength(20);
            entity.HasIndex(x => x.ProviderProductId).IsUnique();
            entity.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = TRUE");
        });

        modelBuilder.Entity<AdminAuditEvent>(entity =>
        {
            entity.ToTable("admin_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(200);
            entity.Property(x => x.ContextJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderDiscrepancy>(entity =>
        {
            entity.ToTable("provider_discrepancies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ProviderActionId).HasMaxLength(200);
            entity.Property(x => x.ProviderStatus).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Key)
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
            entity.HasIndex(x => new { x.Status, x.LastObservedAt });
            entity.HasIndex(x => x.ProviderProductId);
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => x.ProviderParkingActionId);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProviderParkingAction>().WithMany().HasForeignKey(x => x.ProviderParkingActionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderParkingAction>(entity =>
        {
            entity.ToTable("provider_parking_actions");
            entity.HasKey(x => x.Id);
            // These domain fields are staged until the dedicated schema migration.
            entity.Ignore(x => x.Origin);
            entity.Ignore(x => x.VehicleId);
            entity.Ignore(x => x.AssignedUserId);
            entity.Ignore(x => x.AssignmentSource);
            entity.Ignore(x => x.FirstObservedAt);
            entity.Ignore(x => x.LastSyncedAt);
            entity.Property(x => x.ProviderActionId).HasMaxLength(200);
            entity.Property(x => x.ProviderProductId).HasMaxLength(200);
            entity.Property(x => x.ProviderLocation).HasMaxLength(100);
            entity.Property(x => x.ProviderStatus).HasMaxLength(100);
            entity.Property(x => x.ProviderCostAmount);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.HistoryStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => x.ProviderActionId).IsUnique().HasFilter("\"ProviderActionId\" IS NOT NULL");
        });

        modelBuilder.Entity<ProviderOperation>(entity =>
        {
            entity.ToTable("provider_operations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(100);
            entity.Property(x => x.AttemptStartedAt);
            entity.Property(x => x.RequestedEndAt);
            entity.Property(x => x.ParentOperationId);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasIndex(x => x.ParentOperationId);
            entity.HasIndex(x => new { x.ParentOperationId, x.Type, x.ProviderParkingActionId })
                .IsUnique()
                .HasFilter("\"ParentOperationId\" IS NOT NULL");
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProviderParkingAction>().WithMany().HasForeignKey(x => x.ProviderParkingActionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationEvent>(entity =>
        {
            entity.ToTable("notification_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.Type, x.AggregateId }).IsUnique();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.RecipientUserId, x.ReadAt });
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => new { x.RecipientUserId, x.SourceEventId })
                .IsUnique()
                .HasFilter("\"SourceEventId\" IS NOT NULL");
        });


        modelBuilder.Entity<PushDelivery>(entity =>
        {
            entity.ToTable("push_deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.NotificationId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.LastAttemptAt });
            entity.HasOne<Notification>().WithOne().HasForeignKey<PushDelivery>(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Endpoint).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.P256dh).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Auth).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => x.Endpoint).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<VisitEndTimeChange>(entity =>
        {
            entity.ToTable("visit_end_time_changes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Result).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasIndex(x => x.VisitId);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VisitSchedulerWork>(entity =>
        {
            entity.ToTable("visit_scheduler_work");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.AuditReasonCode);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.ProviderParkingActionId)
                .IsUnique()
                .HasFilter("\"ProviderParkingActionId\" IS NOT NULL AND \"Type\" = 'ReconcileProviderAction' AND \"Status\" IN ('Pending', 'Claimed')");
            entity.HasIndex(x => new { x.Status, x.DueAt });
            entity.HasIndex(x => new { x.VisitId, x.Type, x.DueAt })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Pending', 'Claimed') AND \"Type\" <> 'ReconcileProviderAction'");
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProviderParkingAction>().WithMany().HasForeignKey(x => x.ProviderParkingActionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VisitSchedulerAuditEvent>(entity =>
        {
            entity.ToTable("visit_scheduler_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventOrder).IsRequired();
            entity.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GroupKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ReasonCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.Property(x => x.EventKey).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.EventKey).IsUnique();
            entity.HasIndex(x => new { x.VisitId, x.OccurredAt });
            entity.HasIndex(x => new { x.VisitId, x.GroupKey, x.OccurredAt });
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Visit>(entity =>
        {
            entity.ToTable("visits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ProviderProductExternalId).HasMaxLength(200);
            entity.Property(x => x.ProviderLocation).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ProviderProductId);
            entity.OwnsOne(x => x.PolicySnapshot, owned =>
            {
                owned.Property(x => x.MaxPaidParkingDuration).HasColumnName("PolicyMaxPaidParkingDuration");
                owned.Property(x => x.MaxVisitElapsedDuration).HasColumnName("PolicyMaxVisitElapsedDuration");
                owned.Property(x => x.AllowVisitExtension).HasColumnName("PolicyAllowVisitExtension");
                owned.Property(x => x.AllowOpenEndedVisits).HasColumnName("PolicyAllowOpenEndedVisits");
            });
            entity.HasIndex(x => x.StartOperationId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.StartAt });
        });

    }
}
