# SCHED-014 — lock-order inversion tussen schedulerclaim en Visit-mutaties

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Raakt:** schedulerclaim, Stop, end-time changes en andere Visit-mutaties.

## Gewenste invariant

Transacties die dezelfde Visit en schedulerwork combineren moeten locks in een consistente volgorde nemen. Anders kan een correcte functionele race alsnog eindigen in een database-deadlock.

## As-built lockvolgorde

`PostgresVisitSchedulerWorkClaimer.ClaimNextDueAsync` doet:

```text
1. SELECT scheduler work FOR UPDATE SKIP LOCKED
2. pg_advisory_xact_lock(VisitId)
3. Visit herlezen
4. work claimen
```

`ReleaseFailedAsync` gebruikt dezelfde richting: eerst work-row lock, daarna Visit advisory lock.

Andere Visit-mutaties, waaronder `PostgresStopVisitClaimer` en `PostgresVisitEndTimeChanger`, doen juist:

```text
1. pg_advisory_xact_lock(VisitId)
2. Visit/provider/schedulerstate wijzigen
3. daarbij scheduler work rows lezen/updaten
```

## Bewezen risico

Daarmee bestaat de klassieke lock-order inversion:

```text
Scheduler transaction:
  houdt work-row lock
  wacht op Visit advisory lock

Stop/end-time transaction:
  houdt Visit advisory lock
  wacht op dezelfde work-row lock
```

PostgreSQL kan deze cyclus als deadlock detecteren en één transactie aborteren. De worker heeft algemene retry/release-logica, maar een database-deadlock hoort niet als normaal synchronisatiemechanisme te fungeren.

## Impact

- Stop-versus-continuation kan incidenteel falen/retryen op precies de kritieke boundary.
- end-time changes kunnen een transient databasefout krijgen terwijl beide transacties op zichzelf correct zijn.
- betrouwbaarheid wordt timingafhankelijk en moeilijker te reproduceren.
- toekomstige multi-worker schaal vergroot het raceoppervlak.

## Testdekking

Er zijn concurrencytests voor claims en stopgedrag, maar tijdens de audit is geen gerichte test gevonden die deze tegengestelde lockvolgorde forceert en bewijst dat geen deadlock ontstaat.

## Onduidelijkheden / open vragen

1. Welke lock moet architectonisch altijd eerst komen: Visit advisory lock of scheduler work row?
2. Kunnen we due-work selecteren zonder de row lock vast te houden terwijl op de Visit-lock wordt gewacht, zonder claimveiligheid te verliezen?
3. Willen we een expliciete deadlock-retrypolicy op infrastructuurniveau naast een consistente lockvolgorde?

## Richting voor later ontwerp

Eerst één globale lock-orderregel kiezen voor alle Visit-gerelateerde mutaties en die vervolgens in code en integratietests afdwingen. Geen fix binnen deze audit.

## Verificatiecriteria

Een deterministic concurrencytest moet Stop/end-time mutation en schedulerclaim tegen dezelfde Visit laten racen zonder deadlock, duplicate mutation of verloren work.