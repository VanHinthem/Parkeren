# SCHED-006 — open-ended rolling horizon

**Status:** 🧪 Audit afgerond; aanvullend integraal testbewijs gewenst  
**Prioriteit:** middel  
**Scenario:** `DesiredEndAt = null` en geen eerdere harde policygrens.

## Gewenste invariant

Een open-ended Visit mag functioneel onbeperkt blijven lopen totdat hij wordt gestopt of een harde policygrens bereikt. De technische planning mag daarom een eindige horizon gebruiken, maar die horizon mag nooit als functionele eindtijd gaan werken.

## As-built gedrag

`ProviderCoverageSchedule.PlanningEndAt` gebruikt:

1. `DesiredEndAt` wanneer aanwezig;
2. anders `Visit.StartAt + MaxVisitElapsedDuration` wanneer aanwezig;
3. anders `fromAt + 14 dagen`.

Wanneer binnen die horizon geen volgend betaald segment wordt gevonden en de Visit volledig onbegrensd is, wordt het bestaande continuation-work naar het einde van die planninghorizon verplaatst in plaats van voltooid. Daardoor schuift de technische horizon later opnieuw op.

Dezelfde planning wordt gebruikt bij continuation en recovery/rebuild.

## Beoordeling

De gekozen rolling-horizonconstructie is conceptueel correct: 14 dagen is een zoek/planningsvenster en geen Visitlimiet. Er is geen codepad gevonden dat alleen vanwege het bereiken van de 14 dagen de Visit zelf beëindigt.

Wel is de bewijsvoering vooral verspreid over unit/integratiegedrag. Een expliciete integratietest die een volledig open-ended Visit over minstens twee opeenvolgende planninghorizons laat rollen is niet gevonden.

## Relaties

- [SCHED-013](SCHED-013.md): wanneer wél een harde eindgrens geldt moet de Visit ook daadwerkelijk worden afgerond.
- [SCHED-007](SCHED-007.md) en [SCHED-008](SCHED-008.md): open-ended betekent niet dat ingestelde paid/elapsed grenzen genegeerd worden.

## Onduidelijkheden / open vragen

1. Is 14 dagen operationeel de gewenste horizon, of moet die later configureerbaar worden? Dit is geen correctheidsprobleem voor V1.
2. Willen we een expliciete lange-horizon integratietest met een `TimeProvider`, zodat dit zonder echte wachttijd deterministic bewezen wordt?

## Conclusie

Geen zelfstandige functionele fout gevonden. Voor definitieve betrouwbaarheid hoort nog één expliciete rolling-horizon regressietest bij de verbeterfase.