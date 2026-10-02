# SCHED-015 — generieke `Active + Healthy` gating past niet bij ieder work-type

**Status:** ✅ Opgelost via centrale work-type execution policy  
**Prioriteit:** hoog

## Gewenste invariant

Claimability, retry en cancellation worden bepaald door de semantiek van het scheduler-worktype. Providercontinuation, terminal Stop en Long Visit warnings mogen niet dezelfde generieke `Active + Healthy`-regel delen.

## Huidig as-built gedrag

`VisitSchedulerWorkExecutionPolicy` centraliseert de beslissing `Execute`, `Defer` of `Cancel` op basis van work-type, `VisitStatus` en `VisitHealth`.

### `ContinueProviderCoverage`

- `Active + Healthy` -> `Execute`;
- `Active + Reconciling|AttentionRequired|StopFailed` -> `Defer`;
- `Starting` -> `Defer`;
- `Stopping|Completed|Cancelled` -> `Cancel`.

Continuation maakt daardoor geen nieuwe providerdekking wanneer providerstate technisch onzeker is.

### `StopVisit`

Stop wordt niet door health geblokkeerd. Voor `Starting`, `Active` en `Stopping` geldt `Execute`; alleen terminale Visitstatussen worden gecanceld.

Daardoor blijft een functionele eindgrens uitvoerbaar bij `Reconciling`, `AttentionRequired` of `StopFailed`.

### `LongVisitWarning`

Warninggedrag volgt de Visit-lifecycle en is onafhankelijk van health:

- `Starting` -> `Defer`;
- `Active` -> `Execute`;
- `Stopping|Completed|Cancelled` -> `Cancel`.

Een tijdelijke technische healthstatus verwijdert een geplande warning dus niet permanent.

## Consistente toepassing

Dezelfde policy wordt gebruikt door:

- runtime claiming;
- failed-work release/retry;
- startup recovery van achtergelaten `Claimed` work.

Tijdelijk niet-uitvoerbaar work wordt voor V1 standaard één minuut gedeferd; recovery mag eerder opnieuw beoordelen.

## Regressiebewijs

Unit tests dekken de volledige relevante lifecycle/healthmatrix voor alle huidige work-types, inclusief `StopFailed`. Integratietests bewijzen daarnaast dat Stop prioriteit houdt en Long Visit warnings niet door health worden verloren.

SCHED-003, SCHED-004 en SCHED-012 zijn tegen deze policy opnieuw geverifieerd.

## Conclusie

De oorspronkelijke generieke health-gate bestaat niet meer. Schedulerwork heeft nu expliciete, centraal geteste semantiek per work-type en dezelfde policy geldt bij claim, retry en recovery. Er resteert geen zelfstandig SCHED-015-gat.