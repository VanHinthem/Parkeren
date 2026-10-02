# SCHED-007 — `MaxPaidParkingDuration` grens

**Status:** ✅ Opnieuw geverifieerd na terminal lifecycle-hardening  
**Prioriteit:** hoog  

## Gewenste invariant

De totale betaalde tijd binnen één Visit mag de snapshotwaarde `MaxPaidParkingDuration` nooit overschrijden. Gratis tijd telt niet mee. Zodra de maximale betaalde duur is bereikt, mag geen nieuwe providerdekking worden gestart en moet de Visit via de normale terminale lifecycle worden afgerond.

## Huidig as-built gedrag

`VisitTerminalBoundaryCalculator` berekent de functionele grens uit de versioned parkeerregels en het immutable policy-snapshot. Voor `MaxPaidParkingDuration` wordt uitsluitend betaalde tijd opgeteld; gratis perioden en overnight gaps tellen niet mee.

Wanneer de paid-limit de vroegste functionele grens is, levert de calculator:

- `At =` het exacte instant waarop de maximale betaalde duur is verbruikt;
- `Reason = VisitEndReason.MaxPaidParkingDurationReached`.

`VisitTerminalWorkPlanner` bewaakt vervolgens durable, idempotent `StopVisit`-work op die grens. De gewone continuationplanning blijft dezelfde harde grens respecteren en start geen providerdekking voorbij de terminal boundary.

Bij uitvoering van terminal `StopVisit` wordt de Visit via dezelfde Stop/finalization-flow afgerond als andere automatische eindredenen. De functionele `ActualEndAt` komt uit de `DueAt` van de terminale scheduler-work en niet uit een latere wall-clock uitvoertijd.

Startup recovery herberekent de terminal boundary en herbouwt ontbrekende of obsolete pending terminal work voordat de scheduler-loop wordt vrijgegeven.

## Regressiebewijs

De huidige tests bewijzen gezamenlijk:

- `VisitTerminalBoundaryCalculatorTests` dekken de paid-duration boundary en `MaxPaidParkingDurationReached`;
- paid-time calculator/segmenter tests bewijzen dat gratis perioden niet meetellen;
- `VisitSchedulerWorkTests` bewijzen dat terminal Stop-work de eindreden persistent kan dragen;
- `VisitTerminalWorkPlannerTests` dekken idempotentie, replacement en claimed-conflict;
- `VisitTerminalStopExecutionTests` bewijzen dat terminal Stop-work de Visit daadwerkelijk `Completed` maakt en de functionele boundary als `ActualEndAt` bewaart;
- `VisitTerminalRecoveryTests` bewijzen herstel van ontbrekende/verouderde terminal work na restart.

De terminale uitvoeringsflow is niet afhankelijk van een specifieke automatische eindreden; dezelfde persistente reason wordt van work naar Stop-claim en finalization doorgegeven.

## Relaties

- [SCHED-013](SCHED-013.md): centrale terminale Visit lifecycle en eindredenen.
- [SCHED-002](SCHED-002.md): gratis/overnight perioden blijven uitgesloten van betaalde tijd.
- [SCHED-008](SCHED-008.md): elapsed-duration is een onafhankelijke harde grens.

## Conclusie

Het oorspronkelijke lifecycle-gat is door fase B opgelost. `MaxPaidParkingDuration` is niet langer alleen een provider-planningsgrens, maar een duurzame functionele Visitgrens met expliciete eindreden, terminal Stop-work, correcte finalization en restart recovery. Er resteert geen zelfstandig SCHED-007-gat.
