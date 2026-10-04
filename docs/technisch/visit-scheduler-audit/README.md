# Visit scheduler — betrouwbaarheidsaudit

**Status:** SCHED-001 t/m SCHED-017 geïmplementeerd en opnieuw geverifieerd  
**Auditdatum:** 2 oktober 2026  
**SCHED-018 scope:** admin-only, Visit-lifetime retentie en groepering per poging zijn bevestigd
**SCHED-018:** ✅ acceptatie afgerond; persistente events, admin-timeline, correlatie en migratieketen geverifieerd

## Statusboard

| ID | Scenario / bevinding | Status |
| --- | --- | --- |
| SCHED-001 | T-5 aansluitende `StartNewAction` | ✅ |
| SCHED-002 | Gratis periode / overnight hervatten | ✅ |
| SCHED-003 | Handmatig stoppen versus continuation | ✅ |
| SCHED-004 | `DesiredEndAt` verkorten | ✅ |
| SCHED-005 | `DesiredEndAt` verlengen | ✅ |
| SCHED-006 | Open-ended rolling horizon | ✅ |
| SCHED-007 | `MaxPaidParkingDuration` grens | ✅ |
| SCHED-008 | `MaxVisitElapsedDuration` grens | ✅ |
| SCHED-009 | Provider timeout / unknown continuation | ✅ |
| SCHED-010 | Crash/restart tijdens claimed work | ✅ |
| SCHED-011 | Externe providerwijziging / discrepancy | ✅ |
| SCHED-012 | Long Visit warning | ✅ |
| SCHED-013 | Natuurlijke Visit-afronding | ✅ |
| SCHED-014 | Uniforme Visit-first lock-order | ✅ |
| SCHED-015 | Work-type execution policy | ✅ |
| SCHED-016 | Deterministische TwoParkMock boundary harness | ✅ |
| SCHED-017 | Centrale provider identity/timestamp matching | ✅ |
| Scope | Admin-only, Visit-lifetime retentie, poging-groepering | ✅ |
| SCHED-018 | Persistente scheduler observability / audit trail | ✅ |

## Belangrijkste gerealiseerde invarianten

### Lifecycle

Providerdekking en Visit-finalization zijn gescheiden verantwoordelijkheden. Finite Visits krijgen duurzame terminale `StopVisit`-work met persistente `VisitEndReason`. Automatische finalization gebruikt de functionele boundary als `ActualEndAt`.

### Locking

Visit-gerelateerde mutaties gebruiken één Visit-first lock-order. Schedulerclaim selecteert eerst alleen een kandidaat, neemt daarna de Visit advisory lock, lockt vervolgens de exacte work-row en her-valideert.

### Work-type policy

`ContinueProviderCoverage`, `StopVisit` en `LongVisitWarning` hebben expliciet verschillende lifecycle/health-semantiek. Terminal Stop wordt niet geblokkeerd door technische health; continuation wordt bij onveilige health uitgesteld in plaats van blind uitgevoerd.

### JIT continuation

Aaneengesloten betaalde coverage:

```text
precheck        = predecessor.End - 5 minuten
successor.Start = predecessor.End + 1 seconde
```

Gratis gat:

```text
precheck        = nextPaid.Start - 5 minuten
successor.Start = nextPaid.Start
```

Exactly-one/duplicate-prevention, Stop en restart zijn regressiegedekt.

### Provider matching

Centrale `ProviderActionMatchPolicy` gebruikt bekende action-id, productcontext, genormaliseerd kenteken indien relevant, semantische status en 5 seconden timestamp tolerance waar nodig.

De 5 seconden zijn een **engineering margin**, geen gemeten 2Park-SLA. Bekende-ID mismatch valt nooit terug naar een andere action; onbekende-ID fallback accepteert alleen één unieke kandidaat.

### Recovery

V1 is expliciet single-instance. Startup recovery verwerkt achtergelaten claimed work policygedreven, respecteert de attempt lease voor provideroperations, hervat interrupted reconciliation via read-back en herbouwt continuation- en terminal-work vóór nieuwe claims.

### Boundary harness

TwoParkMock ondersteunt een bestuurbare klok, dynamic `scheduled -> active`, visibility delay, timestamp offsets, locationlabel override, expliciete post-End modi en configureerbare scheduled-capacity. Onbewezen live providergedrag krijgt bewust geen default.

## Externe providerpunten die nog niet als contract gelden

De lokale implementatie/regressies voor SCHED-001 t/m 017 zijn afgerond; onderstaande punten zijn nog niet hard live gemeten en vormen geen open scheduler-codegat:

- exacte 2Park status/zichtbaarheid na natuurlijke `End`;
- betrouwbaarheid van een continuation-`StartNewAction` op de gekozen T-5-grens;
- duplicate- en read-back-identificatie na een verloren mutationresponse;
- provider/account-capaciteitslimiet, of `scheduled` meetelt en de overflowresponse;
- provider/action-ID-uniciteitsscope voor een databaseconstraint;
- aanvullende providerfoutcategorieën buiten de bevestigde `PRK-00005` en `PRK-00067`;
- een gemeten timestamp-SLA; de code gebruikt 5 seconden alleen als engineering margin.

De applicatie gebruikt conservatieve identity/reconciliation zodat correctness niet van deze aannames afhangt.

## Documentstructuur

- `SCHED-001.md` t/m `SCHED-017.md` beschrijven de actuele scenario-/bevindingstatus en het regressiebewijs.
- `PROGRESS.md` bevat de implementatie- en verificatievoortgang.
- `*-technical-design.md` en `IMPLEMENTATION-PLAN.md` zijn historische ontwerp-/implementatie-inputs. Zij blijven nuttig voor besluitgeschiedenis, maar zijn **niet** de actuele statusbron wanneer zij oudere formuleringen bevatten.
- SCHED-018-keuzes: admin-only, Visit-lifetime retentie en groepering per poging.
- `SCHED-018.md` bevat de bevestigde scope, geverifieerde implementatie en acceptatieresultaten.

## Volgende stap

SCHED-001 t/m SCHED-018 zijn voor deze iteratie afgerond. De volledige migratieketen is uitsluitend op de wegwerp-Testcontainers-database toegepast; deployment naar een blijvende omgeving blijft een aparte releasehandeling.
