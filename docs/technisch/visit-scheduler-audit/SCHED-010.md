# SCHED-010 — crash/restart tijdens claimed work

**Status:** ❓ Audit afgerond; betrouwbaar bij single-instance, deploymentaanname expliciet maken  
**Prioriteit:** middel/hoog

## Gewenste invariant

Een proces/container-restart mag geen schedulerwork permanent `Claimed` achterlaten en mag geen provider mutation dupliceren. Recovery moet eerst externe onzekerheid oplossen en daarna schedulerwork veilig hervatten.

## As-built gedrag

`VisitSchedulerWorker` start de claim-loop pas nadat `VisitRecoveryService.RecoverAsync` succesvol is afgerond. Mislukte startup recovery wordt herhaald; provider-mutaties worden dus niet hervat terwijl de opstartreconciliatie faalt.

Recovery:

- zet achtergebleven `Claimed` schedulerwork terug naar `Pending` en verwijdert `ClaimedAt`/`ClaimedBy`;
- classificeert unresolved provideroperations;
- reconciliëert `Start`, `ContinueStart`, `Extend`, `Stop` en scheduled-cancel waar mogelijk;
- markeert ambigue situaties als aandachtspunt in plaats van blind opnieuw te muteren;
- rebuildt schedulerwork voor herstelbare actieve Visits.

Integratietests bewijzen dat claimed work na recovery opnieuw pending wordt en dat unknown operations opnieuw worden beoordeeld.

## Open architectuurpunt

`ReleaseClaimedSchedulerWorkAsync` behandelt een startup-claim als achtergelaten work zonder zichtbaar owner/lease/liveness-protocol voor een andere nog levende worker. Dat is correct wanneer V1 exact één API/scheduler-instance heeft.

Bij meerdere gelijktijdige applicatie-instances kan een nieuw opstartende instance echter theoretisch work vrijgeven dat op datzelfde moment door een andere gezonde instance wordt verwerkt. `FOR UPDATE SKIP LOCKED` en `ClaimedBy` maken multi-worker claiming technisch mogelijk, maar startup recovery maakt de single-instance-aanname niet expliciet.

## Relaties

- [SCHED-009](SCHED-009.md): unresolved provider mutations moeten vóór retry worden gereconcilieerd.
- [SCHED-014](SCHED-014.md): consistente locking blijft ook tijdens recovery belangrijk.
- [SCHED-017](SCHED-017.md): recovery is slechts zo betrouwbaar als provider-matching.

## Onduidelijkheden / open vragen

1. Is V1/production expliciet gegarandeerd single-instance voor `Parkeren.Api`/`VisitSchedulerWorker`?
2. Willen we later horizontaal kunnen schalen of rolling deployments met tijdelijk twee instances ondersteunen?
3. Zo ja: moet een schedulerclaim een lease/heartbeat/owner-expiry krijgen in plaats van alle claims bij iedere startup vrij te geven?

## Conclusie

Voor de huidige vermoedelijke single-container deployment is de restart recovery bewust en degelijk opgezet. De deploymentassumptie moet wel expliciet worden vastgelegd; multi-instance gedrag is op dit moment niet bewezen.