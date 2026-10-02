# SCHED-013 — natuurlijke Visit-afronding

**Status:** ✅ Geïmplementeerd en opnieuw geverifieerd  
**Prioriteit:** kritiek/hoog  
**Raakt:** finite Visits, free-only Visits, SCHED-007, SCHED-008 en recovery

## Gewenste invariant

Providerdekking en Visit-finalization zijn twee aparte verantwoordelijkheden. Zodra de vroegste functionele Visitgrens wordt bereikt, moet de Visit via duurzame terminale scheduler-work naar `Stopping -> Completed`, ongeacht of op dat moment nog providerdekking bestaat.

## Huidig as-built gedrag

`VisitTerminalBoundaryCalculator` is de centrale bron voor de vroegste grens uit:

1. `DesiredEndAt`;
2. `Visit.StartAt + MaxVisitElapsedDuration`;
3. de wall-clock boundary waarop `MaxPaidParkingDuration` is opgebruikt.

De tie-break bij exact gelijke grenzen is vastgelegd als:

`MaxPaidParkingDurationReached -> MaxVisitElapsedDurationReached -> DesiredEndReached`.

De eindreden wordt persistent gedragen via `VisitEndReason`:

- `ManualStop`;
- `DesiredEndReached`;
- `MaxVisitElapsedDurationReached`;
- `MaxPaidParkingDurationReached`.

`VisitTerminalWorkPlanner` bewaakt idempotent één actuele `StopVisit`-taak voor een berekenbare terminal boundary. Obsolete pending work wordt vervangen; claimed work wordt niet stil overschreven.

Bij uitvoering gebruikt de bestaande Stop/finalization-flow dezelfde duurzame provideroperation/reconciliation-mechanismen als handmatig stoppen. Voor automatische terminale stops wordt `ActualEndAt` functioneel bepaald uit de scheduler-work `DueAt`; provider-readbacktimestamps verschuiven de Visit-eindtijd niet.

Startup recovery herberekent de boundary en herbouwt ontbrekende of verouderde terminal work vóór nieuwe schedulerclaims worden vrijgegeven. Een reeds verstreken boundary wordt daardoor na restart direct due.

## Schedulerprioriteit

Bij gelijke `DueAt` wint `StopVisit` van continuation en `LongVisitWarning`. Op of na de terminale grens mag geen nieuwe providercontinuation of reminder de Visit voortzetten.

## Regressiebewijs

De huidige tests dekken gezamenlijk:

- DesiredEnd-, elapsed- en paid-duration boundaries;
- paid-time over versioned rulesets, inclusief gratis/overnight perioden;
- tie-breaks;
- idempotente terminal work planning en replacement;
- automatische Stop met persistente `EndReason`;
- functionele boundary als `ActualEndAt`;
- manual Stop met werkelijke stoptijd;
- restart recovery van ontbrekende/obsolete terminal work;
- Stop-prioriteit en Visit-first locking;
- verkorten/verlenging van `DesiredEndAt` zonder obsolete continuation te laten overleven.

SCHED-007 en SCHED-008 zijn op deze lifecycle opnieuw geverifieerd en bevatten geen zelfstandig lifecycle-gat meer.

## Conclusie

Het oorspronkelijke kernprobleem is opgelost. Een functionele Visitgrens is nu onafhankelijk van providerdekking duurzaam gepland, reason-aware, idempotent, restart-safe en gekoppeld aan de bestaande Stop/finalization-orchestration. Er resteert geen zelfstandig SCHED-013-gat.