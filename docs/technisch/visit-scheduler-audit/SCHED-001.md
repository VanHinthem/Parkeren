# SCHED-001 — T-5 en aansluitende `StartNewAction`

**Status:** ✅ Geïmplementeerd en regressiegeverifieerd  
**Prioriteit:** hoog  
**Scenario:** aaneengesloten betaald parkeren met één future successor

## Gewenste invariant

Bij aaneengesloten betaalde providerdekking wordt maximaal één toekomstige successor JIT aangemaakt:

```text
precheck        = predecessor.End - 5 minuten
successor.Start = predecessor.End + 1 seconde
providerstatus  = scheduled vóór Start
```

De +1 seconde is gebaseerd op live 2Park-bewijs: exact aansluitende timestamps werden als overlap geweigerd met `PRK-00005`.

## Huidig as-built gedrag

`ProviderCoverageSchedule.PrecheckAt` bepaalt T-5. `ProviderContinuationStartStore` accepteert een nog actieve predecessor en kan vóór diens End de future successor duurzaam voorbereiden.

De provider mutation gebruikt een persistente `ProviderOperation` en lokale `ProviderParkingAction`. De directe read-back accepteert `scheduled` als geldige bevestigde status.

Dezelfde scheduler work/operation-id wordt bij replay hergebruikt; een duplicate future successor wordt daardoor niet stil aangemaakt.

## Activation boundary

TwoParkMock leidt status dynamisch uit de bestuurbare mockklok af:

```text
now < Start        => scheduled
Start <= now < End => active
```

De activation-boundary regressietest bewijst dat de future successor op zijn Start remote `active` wordt en redundant schedulerwork geen derde provideraction creëert.

## Stop en recovery

Manual Stop kan een reeds scheduled successor vóór zijn start veilig annuleren/stoppen en handelt daarna de actieve predecessor af. Na afloop blijft geen open provideraction over.

Unknown/restart recovery reconciliëert eerst providerstate en creëert geen duplicate successor.

## Provider matching

Future Start gebruikt dezelfde centrale `ProviderActionMatchPolicy` als andere startflows. De 5-seconden timestamp tolerance is een engineering margin, geen gemeten 2Park-SLA.

## Regressiebewijs

Belangrijk bewijs uit de hardening:

- T-5 future scheduled successor + exactly-one invariant;
- `scheduled` direct read-back geaccepteerd;
- Stop van scheduled successor + actieve predecessor (`f581a135`);
- mock activation boundary en duplicate-prevention (`18fc51cd`);
- centrale provider matching en unknown/reconciliation;
- startup/replay zonder duplicate mutation.

## Extern nog onbewezen

Of een future `scheduled` action meetelt voor de echte 2Park-capaciteitslimiet is nog niet hard gemeten. Dit verandert de exactly-one/idempotency-invariant niet; de mock kan beide testmodi expliciet modelleren.

## Conclusie

De oorspronkelijke T-5 exception/retry-inconsistentie en `active`-only read-back zijn opgelost. SCHED-001 heeft geen zelfstandig code- of testgat meer.
