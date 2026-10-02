# SCHED-006 — open-ended rolling horizon

**Status:** ✅ Opnieuw geverifieerd na scheduler-hardening  
**Prioriteit:** middel  
**Scenario:** `DesiredEndAt = null` en geen eerdere harde policygrens.

## Gewenste invariant

Een open-ended Visit mag functioneel onbeperkt blijven lopen totdat hij wordt gestopt of een harde policygrens bereikt. De technische planning mag daarom een eindige horizon gebruiken, maar die horizon mag nooit als functionele eindtijd gaan werken.

## Huidig as-built gedrag

`ProviderCoverageSchedule.PlanningEndAt` gebruikt:

1. `DesiredEndAt` wanneer aanwezig;
2. anders `Visit.StartAt + MaxVisitElapsedDuration` wanneer aanwezig;
3. anders `fromAt + 14 dagen`.

Wanneer binnen die horizon geen volgend betaald segment wordt gevonden en de Visit volledig onbegrensd is, wordt het bestaande `ContinueProviderCoverage`-work naar het einde van die planninghorizon verplaatst in plaats van voltooid. Bij de volgende uitvoering wordt vanaf het nieuwe scheduler-`now` opnieuw 14 dagen vooruit gepland.

De 14 dagen zijn daarmee uitsluitend een technische zoek-/planningshorizon. Er wordt geen `DesiredEndAt`, `ActualEndAt`, terminal `StopVisit` of andere functionele Visitgrens uit afgeleid.

`VisitSchedulerWorkProcessor` gebruikt voor deze tijdsbeslissingen de geïnjecteerde `TimeProvider`, zodat horizonverschuivingen deterministisch getest kunnen worden zonder wall-clock wachttijd.

## Regressiebewijs

De integratietest `OpenEndedRollingHorizonTests.Open_ended_visit_rolls_across_two_planning_horizons_without_terminal_stop` bewijst met echte PostgreSQL en een bestuurbare `TimeProvider`:

1. een volledig open-ended actieve Visit heeft continuation-work zonder functionele eindgrens;
2. bij de eerste uitvoering zonder betaald segment wordt hetzelfde work `Pending` op `now + 14 dagen`;
3. de Visit blijft `Active` en `DesiredEndAt` blijft `null`;
4. er ontstaat geen terminal `StopVisit`;
5. na het verplaatsen van de klok naar de eerste horizon wordt hetzelfde work opnieuw geclaimd;
6. de tweede uitvoering schuift opnieuw exact 14 dagen vooruit;
7. ook na deze tweede horizon blijft de Visit functioneel open-ended en actief.

Daarmee is expliciet bewezen dat de horizon rolling is en niet ongemerkt als maximale Visitduur fungeert.

## Relaties

- [SCHED-013](SCHED-013.md): wanneer wél een harde eindgrens geldt moet de Visit daadwerkelijk terminal worden afgerond.
- [SCHED-007](SCHED-007.md) en [SCHED-008](SCHED-008.md): open-ended betekent niet dat ingestelde paid/elapsed grenzen genegeerd worden.
- recovery/rebuild gebruikt dezelfde coverageplanning en mag de 14-daagse technische horizon evenmin als terminale grens interpreteren.

## Resterende operationele keuze

Of 14 dagen later configureerbaar moet worden is operationele tuning en geen V1-correctheidsprobleem. De regressietest legt uitsluitend de invariant vast dat een gekozen horizon rolling en niet-functioneel terminaal is.

## Conclusie

SCHED-006 is na de scheduler-hardening opnieuw geverifieerd. De technische 14-daagse horizon kan herhaald doorschuiven, beëindigt de Visit niet en maakt geen terminal Stop-work. Er resteert geen zelfstandig SCHED-006-gat.
