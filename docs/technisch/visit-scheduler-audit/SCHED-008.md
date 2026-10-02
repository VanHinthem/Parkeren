# SCHED-008 — `MaxVisitElapsedDuration` grens

**Status:** ✅ Opnieuw geverifieerd na terminal lifecycle-hardening  
**Prioriteit:** hoog  

## Gewenste invariant

Een Visit met `MaxVisitElapsedDuration` mag nooit voorbij `Visit.StartAt + MaxVisitElapsedDuration` doorlopen, ongeacht gratis perioden, extensions of rolling-horizon-logica.

## Huidig as-built gedrag

`VisitTerminalBoundaryCalculator` berekent de elapsed-grens rechtstreeks als:

`Visit.StartAt + MaxVisitElapsedDuration`.

Wanneer dit de vroegste functionele grens is, levert de calculator:

- `At =` de harde elapsed-boundary;
- `Reason = VisitEndReason.MaxVisitElapsedDurationReached`.

Deze grens is gebaseerd op de oorspronkelijke `Visit.StartAt`; verlengen of gratis tijd reset de elapsed-klok niet. Het policy-snapshot blijft immutable voor de actieve Visit.

`VisitTerminalWorkPlanner` bewaakt durable, idempotent `StopVisit`-work op deze grens. De coverageplanning gebruikt dezelfde harde limit en plant geen providerdekking voorbij de terminal boundary.

Bij uitvoering van terminal `StopVisit` wordt de Visit via de centrale Stop/finalization-flow afgerond. De functionele `ActualEndAt` wordt uit de `DueAt` van de terminale work genomen, zodat een late scheduler-uitvoering de functionele eindtijd niet verschuift.

Startup recovery herberekent de terminal boundary en herbouwt ontbrekende of obsolete pending terminal work voordat schedulerclaims weer plaatsvinden.

## Regressiebewijs

De huidige tests bewijzen gezamenlijk:

- `VisitTerminalBoundaryCalculatorTests` dekken elapsed als vroegste grens en de tie-break met andere boundaries;
- `VisitDurationPolicyValidatorTests` bewijzen dat gratis tijd de elapsed-limiet niet omzeilt;
- start/end-time validatie voorkomt gewone wijzigingen voorbij de snapshotlimit;
- `VisitSchedulerWorkTests` bewijzen dat terminal Stop-work de eindreden persistent draagt;
- `VisitTerminalWorkPlannerTests` dekken idempotentie, replacement en claimed-conflict;
- `VisitTerminalStopExecutionTests` bewijzen daadwerkelijke `Completed`-finalization op de functionele boundary;
- `VisitTerminalRecoveryTests` bewijzen restart-herstel van terminal work.

De terminale uitvoeringsflow is reason-agnostisch: `MaxVisitElapsedDurationReached` volgt hetzelfde persistente work → Stop-claim → finalization-pad als andere automatische eindredenen.

## Relaties

- [SCHED-013](SCHED-013.md): centrale terminale Visit lifecycle en eindredenen.
- [SCHED-006](SCHED-006.md): rolling horizon geldt alleen wanneer geen eerdere harde Visitgrens bestaat.
- [SCHED-007](SCHED-007.md): paid-duration is een onafhankelijke harde grens.

## Conclusie

Het oorspronkelijke lifecycle-gat is door fase B opgelost. `MaxVisitElapsedDuration` is nu een duurzame functionele Visitgrens met expliciete eindreden, terminal Stop-work, correcte boundary-finalization en restart recovery. Er resteert geen zelfstandig SCHED-008-gat.
