# Visit scheduler — technische implementatie

De Visit scheduler voert duurzame tijdgestuurde werkzaamheden uit voor een logische `Visit`. De scheduler beslist niet zelfstandig over parkeerbeleid; hij materialiseert de combinatie van Visit-policy, providerproduct en versioned `ParkingRuleSet`.

## Work types

`VisitSchedulerWork` ondersteunt:

| Type | Doel |
| --- | --- |
| `ContinueProviderCoverage` | Providerdekking voorbereiden/voortzetten voor toekomstige betaalde tijd. |
| `StopVisit` | Terminale Visit-afhandeling op een functionele grens. |
| `LongVisitWarning` | Tijdgestuurde waarschuwing/reminder voor een langdurige actieve Visit. |

## Work execution policy

`VisitSchedulerWorkExecutionPolicy` centraliseert de lifecycle/health-semantiek voor claim, retry en recovery.

### ContinueProviderCoverage

- Active + Healthy => `Execute`
- Active + Reconciling/AttentionRequired/StopFailed => `Defer`
- Starting => `Defer`
- Stopping/Completed/Cancelled => `Cancel`

### StopVisit

- Starting/Active/Stopping => `Execute`, onafhankelijk van health
- Completed/Cancelled => `Cancel`

### LongVisitWarning

- Starting => `Defer`
- Active => `Execute`, onafhankelijk van health
- Stopping/Completed/Cancelled => `Cancel`

De standaard defer-delay voor V1 is één minuut.

## Claiming en lock-order

De globale regel is **Visit-first** voor mutaties die Visit + schedulerwork combineren.

`PostgresVisitSchedulerWorkClaimer`:

1. selecteert een kandidaat zonder langdurige row lock;
2. neemt `VisitAdvisoryLock.For(VisitId)`;
3. lockt daarna de exacte schedulerrow `FOR UPDATE`;
4. her-valideert status, due-time en execution policy;
5. claimt alleen wanneer het work nog geldig is.

`ReleaseFailedAsync`, Stop, end-time changes en startup recovery volgen dezelfde Visit-first richting. Daarmee is de eerdere lock-order inversion uit SCHED-014 verwijderd.

Bij gelijke `DueAt` geldt selectieprioriteit:

```text
StopVisit -> ContinueProviderCoverage -> LongVisitWarning
```

## Provider matching

`ProviderActionMatchPolicy` centraliseert provideridentiteit:

1. bekende provider action-id;
2. providerproduct;
3. genormaliseerd kenteken indien callercontext dat vereist;
4. semantisch toegestane status;
5. Start/End binnen 5 seconden wanneer timestamps relevant zijn.

De **5 seconden** zijn een engineering margin, geen gemeten 2Park-SLA.

Een bekende action-id mag nooit fallback-matchen naar een andere action. Zonder bekende action-id is alleen exact één unieke kandidaat toegestaan. Locationcode versus providerlabel is geen identity mismatch.

Stop blijft primair action-id driven. `ExternalProviderAction` detection koppelt onbekende provider IDs niet automatisch via fallback aan lokale actions.

## JIT continuation — aaneengesloten betaald parkeren

Voor Oss geldt `Continuation = StartNewAction`.

`ProviderCoverageSchedule.PrecheckAt(end)` gebruikt T-5. Wanneer aaneengesloten betaalde coverage nodig blijft:

```text
successor.PlannedStartAt = predecessor.PlannedEndAt + 1 seconde
```

`ProviderContinuationStartStore` mag die future successor vóór predecessor-End voorbereiden. Directe read-back accepteert `scheduled` als geldige status.

Idempotency gebruikt het scheduler work-id als stabiele operation/correlation context. Redundant work, retry en restart mogen geen tweede successor creëren.

## Gratis gat / overnight

Wanneer het volgende betaalde segment later begint:

```text
precheck        = nextPaid.Start - 5 minuten
successor.Start = nextPaid.Start
```

Tijdens het gratis gat bestaat geen providerdekking. De +1 seconde-regel geldt alleen voor werkelijk aaneengesloten provideractions, niet over een gratis interval.

## Open-ended rolling horizon

Zonder concrete of eerdere harde Visitgrens gebruikt de planner 14 dagen als technische horizon.

De horizon wordt bij herbeoordeling opnieuw verschoven. Hij creëert geen terminale boundary en beëindigt de Visit niet.

`VisitSchedulerWorkProcessor` gebruikt voor scheduler-tijdsbeslissingen de geïnjecteerde `TimeProvider`; daardoor zijn horizon- en boundarytests deterministisch.

## Terminal Visit work

`VisitTerminalBoundaryCalculator` berekent de effectieve grens. `VisitTerminalWorkPlanner` bewaakt één actuele duurzame `StopVisit`-taak met `VisitEndReason`.

Automatische Stop gebruikt dezelfde `StopVisitFlow` als manual Stop. De finalizer gebruikt voor een automatische stop de scheduler `DueAt` als functionele `ActualEndAt`.

## Recovery

`VisitSchedulerWorker` claimt geen nieuw work totdat `VisitRecoveryService.RecoverAsync` succesvol is.

Startup recovery:

- herstelt achtergelaten `Claimed` work via dezelfde execution policy;
- respecteert de 5-minuten attempt lease voor `InProgress` provideroperations;
- zet stale attempts eerst naar `Unknown` en reconciliëert daarna;
- hervat onderbroken `Reconciling` via read-back zonder mutation te herhalen;
- herbouwt continuation- en terminal-work;
- verwerkt provider discrepancies conservatief.

Periodieke reconciliation controleert unknown operations en actieve provideractions zonder de schedulerworker definitief te stoppen bij een tijdelijke providercheckfout.

## Discrepancy

Provider end-drift binnen 5 seconden blijft gezond; drift buiten de tolerance kan `ProviderActionEndMismatch` opleveren. Externe Stop wordt lokaal verwerkt en kan de Visit naar `AttentionRequired` brengen.

Continuation wordt dan via de execution policy uitgesteld. Terminal Stop-work wordt niet door die healthstatus geblokkeerd.

## TwoParkMock boundary harness

De mock ondersteunt inmiddels:

- centrale bestuurbare mockklok;
- `scheduled -> active` op basis van read-backtijd;
- visibility delay;
- Start/End read-back offsets;
- afwijkend locationlabel;
- expliciete post-End modi `keep-active`, `completed`, `hide`;
- configureerbaar meetellen van scheduled actions voor capaciteit;
- persisted actions en unknown-outcome scenarios.

Post-End gedrag en scheduled-capacity hebben bewust geen providerdefault zolang live 2Park dat niet heeft bevestigd.

## V1 deploymentcontract

Exact één actieve `Parkeren.Api` / `VisitSchedulerWorker` instance. Geen horizontale schaal en geen overlappende rolling deployment in V1.

## Nog niet geïmplementeerd

SCHED-018: persistente Visit-gerelateerde scheduler observability/audit trail. Dit is bewust de eerstvolgende aparte ontwerp-/implementatiestap en maakt geen deel uit van de huidige scheduler-hardening.
