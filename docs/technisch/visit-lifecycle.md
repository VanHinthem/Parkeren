# Visit lifecycle — technische implementatie

## Persistente kern

`Visit` bewaart functionele parkeerintentie, lifecycle, health, policy snapshot en providerproductcontext. `ProviderParkingAction` representeert één concrete provideraction; `ProviderOperation` maakt provider-mutaties duurzaam en replay-safe.

De backend blijft autoritatief voor policy, parkeerregels, capaciteit en providerstate.

## Lifecycle

```text
Starting -> Active -> Stopping -> Completed
            \-> Cancelled
```

Lifecycle en health zijn orthogonaal. Health kan onder andere `Healthy`, `Reconciling`, `AttentionRequired` of `StopFailed` zijn zonder de functionele lifecycle te vervangen.

## VisitEndReason

Terminale Visits bewaren de oorzaak via `VisitEndReason`:

- `ManualStop`;
- `DesiredEndReached`;
- `MaxVisitElapsedDurationReached`;
- `MaxPaidParkingDurationReached`.

`Visit.BeginStopping(reason)` maakt die oorzaak onderdeel van de duurzame lifecycle.

## Centrale terminal boundary

`VisitTerminalBoundaryCalculator` bepaalt één effectieve terminale boundary uit:

1. `DesiredEndAt`;
2. `StartAt + MaxVisitElapsedDuration`;
3. de wall-clock boundary waarop `MaxPaidParkingDuration` is verbruikt.

Paid-time wordt over versioned parkeerregels berekend; gratis/overnight tijd telt niet mee.

Bij gelijke boundaries geldt de vaste tie-break:

```text
MaxPaidParkingDurationReached
-> MaxVisitElapsedDurationReached
-> DesiredEndReached
```

Een volledig open-ended Visit zonder deze grenzen heeft geen vooraf bekende terminale boundary.

## Durable terminal work

`VisitTerminalWorkPlanner` bewaakt idempotent één actuele terminale `StopVisit`-taak wanneer een boundary bestaat.

- obsolete pending terminal-work wordt vervangen;
- claimed terminal-work wordt niet stil overschreven;
- een end-time change herberekent de boundary;
- startup recovery herbouwt ontbrekende of verouderde terminal-work.

Providerdekking en Visit-finalization blijven daarmee gescheiden verantwoordelijkheden.

## Finalization

Automatisch en handmatig eindigen hergebruiken dezelfde Stop Visit-orchestration:

```text
claim Stop
-> Visit naar Stopping
-> alle open active/scheduled/unknown provideractions afhandelen
-> uncertain outcomes reconciliëren
-> pas zonder open providerwerk: Visit Completed
```

Voor automatische terminale stops gebruikt `StopVisitFinalizer` de `DueAt` van de matchende terminal scheduler-work als functionele `ActualEndAt`. Voor manual stop blijft `ActualEndAt` het daadwerkelijke stopmoment.

## Continuation versus terminal lifecycle

`ContinueProviderCoverage` mag providerdekking uitsluitend vóór de effectieve terminale boundary plannen. Wanneer geen verdere providerdekking nodig is, betekent dat niet dat de Visit vanzelf is afgerond; terminal work verzorgt die verantwoordelijkheid.

Op een gelijke `DueAt` heeft `StopVisit` claimprioriteit boven continuation en `LongVisitWarning`.

## End-time changes

### Verkorten

De end-time changer serialiseert op de Visit en:

- vervangt de terminale boundary;
- annuleert obsolete continuation;
- annuleert/vervangt scheduled successors die de nieuwe grens overschrijden;
- plant zo nodig providerstop op de nieuwe boundary.

### Verlengen

De terminale boundary verschuift alleen wanneer `DesiredEndAt` werkelijk de leidende grens is. Een eerdere elapsed- of paid-durationgrens blijft leidend. Extra providerdekking wordt uitsluitend voor nieuwe betaalde tijd ingepland.

## Manual Stop race

Manual Stop en schedulerclaim volgen dezelfde Visit-first lock-order. Zodra manual Stop de Visit naar `Stopping` heeft gebracht, kan continuation die uitkomst niet terugzetten naar `Active`.

De Stop-flow handelt ook een al aangemaakte future scheduled successor af.

## Recovery

`VisitRecoveryService` is startup gate voor de schedulerworker. Recovery herstelt onder andere:

- achtergelaten schedulerclaims;
- stale `InProgress` provideroperations na de bestaande attempt lease;
- onderbroken `Reconciling` operations;
- ontbrekende continuationplanning;
- terminale Visit-work;
- provider discrepancies.

Er wordt nooit blind opnieuw gemuteerd wanneer de provideruitkomst onzeker is.

## V1 deploymentcontract

V1 ondersteunt exact één actieve `Parkeren.Api` / `VisitSchedulerWorker` instance. Multi-instance/rolling-overlap vereist later een expliciete distributed lease/liveness-oplossing en hoort niet bij het huidige contract.

## Status

De lifecycle-hardening uit SCHED-013 en de gekoppelde SCHED-003/004/005/007/008-scenario's is geïmplementeerd en opnieuw geverifieerd. Persistente scheduler-observability (SCHED-018) is afgerond; zie het [SCHED-018-statusdocument](visit-scheduler-audit/SCHED-018.md).
