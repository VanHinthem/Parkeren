# Visit scheduler — betrouwbaarheidsaudit

**Status:** inhoudelijke audit afgerond; verbeterontwerp en fixes nog open  
**Auditdatum:** 2 oktober 2026  
**Scope:** scheduler, continuation, stop, recovery, Visit-eindgrenzen en tijdgestuurde scheduler-work

Deze audit toetst de actuele implementatie op `main` aan het functionele model, de technische flow, bestaande tests en reeds uitgevoerde echte 2Park-tests. Tijdens deze audit is **geen productielogica gewijzigd**.

Doel is eerst het hele systeembeeld te hebben, zodat gerelateerde bevindingen gezamenlijk kunnen worden opgelost in plaats van lokaal per scenario te patchen.

## Documentstructuur

- `README.md` — statusboard, hoofdbevindingen, relaties en latere verbeterclusters.
- `SCHED-001.md` t/m `SCHED-012.md` — de oorspronkelijke functionele audit-scenario's.
- `SCHED-013.md` t/m `SCHED-017.md` — gedeelde systeembevindingen die tijdens de scenario-audit zichtbaar zijn geworden.

Per document staat waar relevant een sectie **Onduidelijkheden / open vragen**. Die punten zijn bewust niet ingevuld met aannames.

## Auditmethode

Per scenario is gekeken naar:

1. gewenste invariant;
2. daadwerkelijke codeflow;
3. persistente state en locking;
4. providergrens en idempotency;
5. fout/crash/restartgedrag;
6. bestaande geautomatiseerde tests;
7. ontbrekend bewijs;
8. relaties met andere bevindingen;
9. open vragen;
10. verificatiecriteria voor een latere fix.

## Statusbetekenis

| Status | Betekenis |
| --- | --- |
| ⚠️ Bevinding bevestigd | Concrete inconsistentie/risico aangetoond; fix nog niet ontworpen of uitgevoerd. |
| ✅ Audit afgerond | Geen zelfstandige afwijking gevonden; document kan wel afhankelijkheden naar andere bevindingen hebben. |
| 🧪 Testbewijs aanvullen | Geen concrete productiefout gevonden, maar bewijs is nog onvoldoende volledig. |
| ❓ Open architectuur/providerpunt | Correct gedrag hangt af van een expliciete nog onbewezen aanname of extern contract. |

## Auditstatus

| ID | Scenario / bevinding | Status | Prioriteit | Detail |
| --- | --- | --- | --- | --- |
| SCHED-001 | T-5 → aansluitende `StartNewAction` | ⚠️ | hoog | [SCHED-001](SCHED-001.md) |
| SCHED-002 | Gratis periode / overnight → hervatten betaald parkeren | ⚠️ | hoog | [SCHED-002](SCHED-002.md) |
| SCHED-003 | Handmatig stoppen versus continuation | ✅ afhankelijk | hoog via gedeelde punten | [SCHED-003](SCHED-003.md) |
| SCHED-004 | `DesiredEndAt` verkorten | ⚠️ afhankelijk | hoog | [SCHED-004](SCHED-004.md) |
| SCHED-005 | `DesiredEndAt` verlengen | ✅ afhankelijk | middel | [SCHED-005](SCHED-005.md) |
| SCHED-006 | Open-ended rolling horizon | 🧪 | middel | [SCHED-006](SCHED-006.md) |
| SCHED-007 | `MaxPaidParkingDuration` grens | ⚠️ via lifecycle | hoog | [SCHED-007](SCHED-007.md) |
| SCHED-008 | `MaxVisitElapsedDuration` grens | ⚠️ via lifecycle | hoog | [SCHED-008](SCHED-008.md) |
| SCHED-009 | Provider timeout / unknown continuation | ⚠️ via matching | hoog | [SCHED-009](SCHED-009.md) |
| SCHED-010 | Crash/restart tijdens claimed work | ❓ | middel/hoog | [SCHED-010](SCHED-010.md) |
| SCHED-011 | Externe providerwijziging / discrepancy | ✅ afhankelijk | middel | [SCHED-011](SCHED-011.md) |
| SCHED-012 | Long Visit warning schedulergedrag | ⚠️ | middel | [SCHED-012](SCHED-012.md) |
| SCHED-013 | Natuurlijke Visit-afronding ontbreekt | ⚠️ | **kritiek/hoog** | [SCHED-013](SCHED-013.md) |
| SCHED-014 | Lock-order inversion schedulerwork ↔ Visit-lock | ⚠️ | **hoog** | [SCHED-014](SCHED-014.md) |
| SCHED-015 | Generieke `Active + Healthy` gating per work-type onjuist | ⚠️ | **hoog** | [SCHED-015](SCHED-015.md) |
| SCHED-016 | TwoParkMock modelleert tijdsstatussen onvoldoende | ⚠️ testmodel | middel/hoog | [SCHED-016](SCHED-016.md) |
| SCHED-017 | Timestampmatching strenger dan live 2Park-gedrag | ⚠️ | **hoog** | [SCHED-017](SCHED-017.md) |

## Kritieke hoofdbevindingen

### 1. Providercontinuation is conceptueel goed, maar timingketen is niet consistent

De live 2Park-tests hebben bewezen dat één toekomstige successor vanaf T-5 mogelijk is en dat `Start = predecessor.End + 1 seconde` werkt. De huidige store blokkeert die call tot ná predecessor-end. Daarnaast accepteert de directe read-back geen geldige `scheduled` status. SCHED-001 en SCHED-002 moeten daarom als één timing/schedulingprobleem worden ontworpen.

### 2. Provideridentiteit en timestamps zijn te strak gekoppeld

Live 2Park kan timestamps enkele seconden normaliseren, terwijl meerdere codepaden exact of binnen 1 ms vergelijken. Daardoor kan een correcte mutation in `Unknown`, `Reconciling` of discrepancy blijven hangen. SCHED-017 raakt rechtstreeks SCHED-001, SCHED-009 en SCHED-011.

### 3. Providerdekking eindigen is niet hetzelfde als de Visit beëindigen

De scheduler kan correct vaststellen dat geen nieuwe provideraction nodig is, maar de logische Visit krijgt bij een gewone natuurlijke eindtijd niet vanzelf een terminale lifecycle. Dat raakt finite Visits, free-only Visits en beide harde policygrenzen. Dit is SCHED-013 en behoort vóór V1 opgelost te worden.

### 4. Concurrency primitives zijn goed gekozen maar lockvolgorde is niet uniform

Schedulerclaim gebruikt work-row → Visit advisory lock. Stop/end-time-mutaties gebruiken Visit advisory lock → schedulerwork. Die inversie kan deadlocks veroorzaken. Zie SCHED-014.

### 5. Schedulerwork heeft niet één uniforme healthsemantiek

`Active + Healthy` is een logische safety gate voor continuation, maar niet vanzelf voor `StopVisit` of `LongVisitWarning`. De generieke gate kan juist veiligheidskritiek Stop-work annuleren. Zie SCHED-015.

### 6. Onze mock kan de belangrijkste tijdsgrenzen nog niet echt bewijzen

TwoParkMock zet `scheduled`/`active` alleen bij creatie en laat statuses niet vanzelf met de tijd overgaan. Daardoor kunnen scheduler-boundary tests een onrealistisch providerbeeld gebruiken. Zie SCHED-016.

## Wat juist sterk is in de huidige implementatie

De audit laat ook zien dat de backend niet vanaf nul opnieuw ontworpen hoeft te worden. Sterke bouwstenen zijn:

- persistente `VisitSchedulerWork`;
- durable `ProviderOperation` vóór externe mutations;
- operation-idempotency/replay;
- unknown → reconciliation in plaats van blind retry;
- startup recovery als gate vóór nieuwe schedulerclaims;
- PostgreSQL `FOR UPDATE SKIP LOCKED`;
- Visit advisory locks;
- duurzame Stop Visit-flow die alle provideractions afhandelt vóór Visit-finalization;
- duurzame cancel/replace-logica voor end-time shortening;
- versioned rulesets en betaalde/gratis segmentatie;
- policy snapshot per Visit;
- persistente provider discrepancy-detectie.

De verbeterfase moet deze bouwstenen behouden en vooral de semantiek ertussen consistenter maken.

## Relatie-/impactmatrix

| Verbetercluster | Primaire IDs | Raakt daarnaast |
| --- | --- | --- |
| JIT/scheduled providercoverage | SCHED-001, SCHED-002 | 003, 004, 005, 009 |
| Provider matching/normalisatie | SCHED-017 | 001, 009, 011 |
| Visit lifecycle/finalization | SCHED-013 | 002, 007, 008, 011, 012 |
| Locking/concurrency | SCHED-014 | 003, 004, 005, 010 |
| Work-type state policy | SCHED-015 | 003, 004, 012 |
| Realistische provider test harness | SCHED-016 | 001, 002, 009, 011 |
| Recovery/deploymentmodel | SCHED-010 | 009, 014 |

## Aanbevolen volgorde ná deze audit

Nog **geen codewijziging** op basis van één los SCHED-item. Eerst een gezamenlijk verbeterontwerp maken in deze volgorde:

1. lifecycle-invarianten en terminale Visitstatus (`SCHED-013`);
2. provideraction state/timing-contract inclusief scheduled successor (`SCHED-001`, `002`, `017`);
3. work-type state/health policy (`SCHED-015`);
4. uniforme lock-order (`SCHED-014`);
5. recovery/deploymentaanname (`SCHED-009`, `010`);
6. TwoParkMock en integrale boundary-tests (`SCHED-016`);
7. daarna scenario voor scenario regressieverificatie van SCHED-001 t/m SCHED-012.

## Algemene auditconclusie

De backend bevat veel goede reliability-mechanismen, vooral rond durable provideroperations, recovery en Stop. Het huidige geheel verdient echter nog geen V1-betrouwbaarheidsvink door enkele systeemoverstijgende inconsistenties rond lifecycle, scheduled timing, provider-matching, lock-order en work-type gating.

De audit is inhoudelijk afgerond. De volgende stap is een **geconsolideerd technisch verbeterontwerp**, waarna fixes in samenhang kunnen worden geïmplementeerd en per SCHED-item geverifieerd.