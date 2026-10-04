# Fase 6 — Scheduler, continuation en recovery

**Status: functioneel afgerond en later scheduler-gehard ✅**  
**Oorspronkelijke fase-exit:** 30 september 2026  
**Scheduler-hardening opnieuw geverifieerd:** 2 oktober 2026

## Doel

De Visit-lifecycle onafhankelijk maken van een geopende PWA of in-memory timers. Providerdekking, toekomstige acties, retries en herstel zijn persistent en server-side georkestreerd.

## Opgeleverd

- persistente `VisitSchedulerWork`;
- `VisitSchedulerWorker` met startup recovery gate;
- JIT provider-continuation op T-5;
- paid/free-segmentatie over versioned rulesets;
- rolling 14-daagse planninghorizon voor volledig open-ended Visits;
- duurzame `ProviderOperation` correlation/idempotency;
- unknown outcome -> read-back/reconciliation in plaats van blind retry;
- restart recovery;
- provider discrepancy-detectie;
- duurzame cancel/replace bij end-time changes;
- terminale Visit-lifecycle met `VisitEndReason` en durable `StopVisit` work;
- Visit-first lock-order voor scheduler/Stop/end-time/recovery;
- centrale work-type execution policy;
- centrale provider action matching met 5-seconden engineering tolerance;
- deterministische TwoParkMock boundary harness.

## Oss continuation

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
```

### Aaneengesloten betaald

```text
precheck        = predecessor.End - 5 minuten
successor.Start = predecessor.End + 1 seconde
```

Live 2Park-tests bevestigden dat exact aansluitende acties als overlap kunnen worden geweigerd en dat een future action als `scheduled` kan worden aangemaakt.

### Gratis gat

```text
precheck        = nextPaid.Start - 5 minuten
successor.Start = nextPaid.Start
```

Tijdens gratis tijd bestaat geen providerdekking; de logische Visit kan wel actief blijven.

## Terminale Visitgrens

De vroegste toepasselijke grens uit `DesiredEndAt`, `MaxVisitElapsedDuration` en `MaxPaidParkingDuration` krijgt duurzame terminale `StopVisit`-work.

Automatische finalization gebruikt de functionele boundary als `ActualEndAt`. Manual Stop gebruikt het werkelijke stopmoment.

## Locking en scheduler policy

Mutaties rond dezelfde Visit volgen Visit-first locking. Schedulerclaim selecteert eerst een kandidaat, neemt daarna de Visit advisory lock en vervolgens de exacte work-row lock.

Work-types hebben eigen semantiek:

- continuation wordt bij onveilige health uitgesteld;
- terminal Stop blijft uitvoerbaar ondanks technische health;
- Long Visit warning volgt lifecycle en niet generieke providerhealth.

## Provider identity

Matching gebruikt centraal known action-id, productcontext, normalized plate indien relevant, semantische status en 5 seconden timestamp tolerance indien timestamps relevant zijn.

De 5 seconden zijn een engineering margin, geen gemeten provider-SLA. Bekende-ID mismatch valt nooit terug naar een andere action; onbekende-ID fallback vereist één unieke kandidaat.

## Recovery

V1 draait single-instance. Startup recovery:

- herstelt achtergelaten claimed work policygedreven;
- respecteert de provider attempt lease;
- brengt stale `InProgress` eerst naar `Unknown` en reconciliëert via read-back;
- hervat interrupted reconciliation zonder repeat mutation;
- herbouwt continuation- en terminal-work.

## Testharness

TwoParkMock heeft een bestuurbare klok, dynamic `scheduled -> active`, visibility delay, read-back timestamp offsets, locationlabel override, expliciete post-End modi en configureerbaar meetellen van scheduled actions voor capaciteit.

Onbewezen live providersemantiek wordt niet als mockdefault vastgelegd.

## Fase-exit na hardening

SCHED-001 t/m SCHED-017 zijn geïmplementeerd en scenario-voor-scenario opnieuw geverifieerd. Daarmee is scheduler correctness voor V1 op de huidige single-instance architectuur opnieuw afgedekt.

SCHED-018 — persistente scheduler observability/audit trail — is als aparte observabilitystap afgerond. De admin-only Visit-timeline, eventcorrelatie, reason-catalogus en migratieketen zijn geverifieerd; zie het actuele bewijs in `docs/technisch/visit-scheduler-audit/SCHED-018.md`.
