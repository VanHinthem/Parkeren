# Visit scheduler — betrouwbaarheidsaudit

**Status:** in uitvoering  
**Start:** 2 oktober 2026  
**Scope:** scheduler, continuation, stop, recovery en tijdgestuurde scheduler-work

Deze audit toetst de actuele schedulerimplementatie op `main` scenario voor scenario. Functionele en technische scheduler-documentatie beschrijven het bedoelde en as-built gedrag; deze audit beoordeelt vervolgens of de implementatie de gewenste invarianten betrouwbaar afdwingt.

Tijdens de audit wordt niet stilzwijgend gerefactord. Eerst wordt gedrag bewezen, testdekking vastgesteld en een bevinding vastgelegd. Pas daarna wordt besloten of de implementatie moet wijzigen.

## Documentstructuur

- `README.md` — dit hoofddocument; status, auditmethode, kritieke aandachtspunten en backlog.
- `SCHED-xxx.md` — één document per concrete auditbevinding/scenario.

## Auditmethode

Per scenario beoordelen we:

1. gewenste functionele invariant;
2. daadwerkelijke codeflow;
3. persistente state en locks;
4. providergrens en idempotency;
5. gedrag bij fout/crash/restart;
6. bestaande geautomatiseerde tests;
7. ontbrekende testdekking;
8. conclusie;
9. eventueel besluit/fix;
10. verificatie na fix.

## Statuswaarden

| Status | Betekenis |
| --- | --- |
| ⬜ Nog te auditen | Scenario nog niet inhoudelijk onderzocht. |
| 🔎 In onderzoek | Code/test/providergedrag wordt onderzocht. |
| ⚠️ Bevinding bevestigd | Risico of inconsistentie is bewezen; besluit/fix nog open. |
| 🛠 Fix gepland | Gewenste oplossing is vastgesteld maar nog niet volledig geïmplementeerd. |
| 🧪 Te verifiëren | Fix aanwezig; aanvullende tests/validatie nog nodig. |
| ✅ Afgerond | Gedrag, tests en eventuele fix zijn voldoende bewezen. |

## Kritieke aandachtspunten

Deze punten zijn nog niet automatisch bugs, maar zijn betrouwbaarheidskritisch en moeten tijdens de audit expliciet worden beoordeeld:

1. **Continuation rond providergrenzen** — T-5, scheduled/start timing, overlap en eventuele gaten tussen actions.
2. **Stop versus continuation** — Stop moet altijd winnen zonder dat alsnog een opvolgaction ontstaat.
3. **Idempotency bij provider-timeouts** — een onzekere externe mutatie mag nooit blind als nieuwe mutatie worden herhaald.
4. **Crash/restart recovery** — claimed work, unresolved `ProviderOperation` en providerstate moeten deterministisch worden hersteld.
5. **Gratis/betaalde overgangen** — geen onnodige providerdekking in gratis perioden en tijdig hervatten bij volgende betaalde periode.
6. **Policygrenzen** — `MaxPaidParkingDuration` en `MaxVisitElapsedDuration` mogen nooit via scheduler/retry overschreden worden.
7. **End-time changes** — verkorten en verlengen moeten bestaand schedulerwerk correct vervangen/cancellen.
8. **Visit health gating** — beoordelen of `Active + Healthy` voor ieder scheduler-worktype de juiste semantiek is, met name `LongVisitWarning`.
9. **Kloktijdconsistentie** — code gebruikt zowel `TimeProvider` als directe `DateTimeOffset.UtcNow`; beoordelen op testbaarheid en boundary-races.
10. **Processorcomplexiteit** — `VisitSchedulerWorkProcessor` orkestreert veel verantwoordelijkheden; beoordelen of dit risico oplevert voor foutisolatie en testbaarheid.

## Auditstatus

| ID | Scenario / bevinding | Status | Detail |
| --- | --- | --- | --- |
| SCHED-001 | T-5 → aansluitende `StartNewAction` | ⚠️ Bevinding bevestigd | [SCHED-001](SCHED-001.md) |
| SCHED-002 | Gratis periode / overnight → hervatten betaald parkeren | ⬜ Nog te auditen | — |
| SCHED-003 | Handmatig stoppen versus continuation | ⬜ Nog te auditen | — |
| SCHED-004 | `DesiredEndAt` verkorten | ⬜ Nog te auditen | — |
| SCHED-005 | `DesiredEndAt` verlengen | ⬜ Nog te auditen | — |
| SCHED-006 | Open-ended rolling horizon | ⬜ Nog te auditen | — |
| SCHED-007 | `MaxPaidParkingDuration` grens | ⬜ Nog te auditen | — |
| SCHED-008 | `MaxVisitElapsedDuration` grens | ⬜ Nog te auditen | — |
| SCHED-009 | Provider timeout / unknown continuation | ⬜ Nog te auditen | — |
| SCHED-010 | Crash/restart tijdens claimed work | ⬜ Nog te auditen | — |
| SCHED-011 | Externe providerwijziging / discrepancy | ⬜ Nog te auditen | — |
| SCHED-012 | Long Visit warning schedulergedrag | ⬜ Nog te auditen | — |

## Algemene auditconclusie

Nog niet vastgesteld. De scheduler krijgt pas een algemene betrouwbaarheidsconclusie wanneer de kernscenario's hierboven zijn onderzocht en alle kritieke bevindingen zijn opgelost of expliciet geaccepteerd.
