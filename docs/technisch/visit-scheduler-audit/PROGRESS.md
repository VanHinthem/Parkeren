# Scheduler hardening — voortgang

**Laatst bijgewerkt:** 2 oktober 2026

Dit bestand wordt vanaf de implementatiefase bijgewerkt in dezelfde logische commits als de codewijzigingen. Er worden geen aparte voortgangscommits gemaakt.

## Fase A — state policy en locking

### A1 — SCHED-015 work-type execution policy

- ✅ `VisitSchedulerWorkExecutionPolicy` toegevoegd.
- ✅ Volledige status/health-matrix unit-getest, inclusief `StopFailed`.
- ✅ CI groen op commit `fa697924` (`test: cover scheduler work execution policy`).
- ✅ `PostgresVisitSchedulerWorkClaimer` aangesloten op `Execute` / `Defer` / `Cancel`.
- ✅ `Pending` work kan semantisch worden uitgesteld zonder kunstmatig claim/release-pad.
- ✅ CI groen op commit `82ea3ba6` (`fix: apply scheduler work execution policy`).
- ✅ `ReleaseFailedAsync` gebruikt dezelfde policy; `Execute` en `Defer` releasen voor retry, alleen `Cancel` annuleert definitief.
- ✅ CI groen op commit `7554ceb2` (`fix: apply scheduler retry policy`).

Besluit defer-delay voor V1: tijdelijk niet-uitvoerbaar schedulerwork wordt standaard **1 minuut** uitgesteld. Recovery mag eerder herbeoordelen. De delay staat op één plek in de claimer en kan later eenvoudig configureerbaar worden gemaakt als operationele tuning dat nodig maakt.

### A2 — SCHED-014 uniforme lock-order

- ✅ `ClaimNextDueAsync` gebruikt tweefasenclaim: kandidaat zonder row lock, daarna Visit advisory lock, daarna exacte scheduler-row `FOR UPDATE` en her-validatie.
- ✅ Bij gelijke `DueAt` geldt selectieprioriteit `StopVisit -> ContinueProviderCoverage -> LongVisitWarning`.
- ✅ CI groen op commit `7c86a9f8` (`fix: enforce visit-first scheduler claim locking`).
- ✅ `ReleaseFailedAsync` gebruikt dezelfde Visit-first lock-order en revalideert pas na de row lock.
- ✅ CI groen op commit `69675b38` (`fix: enforce visit-first scheduler retry locking`).
- ✅ PostgreSQL-racetests toegevoegd voor twee workers, claim versus manual Stop, claim versus end-time change en gelijke `DueAt`-prioriteit.
- ✅ CI groen op commit `4023c6e3` (`test: cover scheduler locking races`).
- ✅ SCHED-014 afgerond.

## Fase B — terminale Visit lifecycle ✅ AFGEROND

### B1 — SCHED-013 Visit end reason

- ✅ `VisitEndReason` toegevoegd met `ManualStop`, `DesiredEndReached`, `MaxVisitElapsedDurationReached` en `MaxPaidParkingDurationReached`.
- ✅ `Visit.BeginStopping(reason)` legt de reden persistent vast; bestaande parameterloze `BeginStopping()` blijft compatibel en betekent `ManualStop`.
- ✅ Domeintests dekken expliciete reden en immutable lifecycle-semantiek.
- ✅ CI groen op commit `baee2000` (`feat: add visit end reason`).

### B2 — centrale terminal boundary

- ✅ `VisitTerminalBoundaryCalculator` bepaalt de vroegste functionele grens uit DesiredEndAt, MaxVisitElapsedDuration en MaxPaidParkingDuration.
- ✅ Paid-time gebruikt versioned parkeerregels; gratis/overnight tijd telt niet mee.
- ✅ Tie-break vastgelegd als `MaxPaid -> MaxElapsed -> DesiredEnd`.
- ✅ Unit tests dekken desired/elapsed/paid, overnight, tie-break en onbeperkte Visits.
- ✅ CI groen na gerichte testfixes op commit `91b93086`.

### B3 — terminal scheduler work

- ✅ `VisitSchedulerWork.EndReason` toegevoegd; alleen `StopVisit` work mag een eindreden dragen.
- ✅ `VisitTerminalWorkPlanner.EnsureAsync` bewaakt idempotent precies één actuele terminale Stop-taak en vervangt alleen pending obsolete work.
- ✅ Claimed terminal work wordt nooit stil vervangen.
- ✅ Domein- en PostgreSQL-integratietests toegevoegd voor metadata, idempotentie, replacement en claimed conflict.
- ✅ Terminal planning aangesloten op Visit start en `DesiredEndAt`-wijzigingen; oude ad-hoc StopVisit-planning verwijderd.
- ✅ CI groen op commit `8d358ada` na fixture- en PostgreSQL timestamp-precisiefixes.

### B4 — terminal stop execution, finalization en recovery

- ✅ `StopVisitCommand` kan een expliciete `VisitEndReason` dragen; ontbrekende reden blijft compatibel en betekent `ManualStop`.
- ✅ `PostgresStopVisitClaimer` gebruikt de expliciete eindreden bij de overgang naar `Stopping`.
- ✅ CI groen op commit `843228ca` (`feat: carry visit end reason through stop claim`).
- ✅ Als de schedulercommand geen reden meegeeft, resolveert de claimer de reden uit de duurzame `StopVisit`-work met hetzelfde operation/work-id.
- ✅ Replays valideren dat een gevonden terminale reden niet conflicteert met de reeds vastgelegde Visit-redenen.
- ✅ `StopVisitFinalizer` gebruikt voor automatische terminale stops de `DueAt` van de matchende scheduler-work als functionele `ActualEndAt`; manual stop blijft de werkelijke stoptijd gebruiken.
- ✅ CI groen op commit `76103a03` (`fix: finalize visits at terminal boundary`).
- ✅ PostgreSQL-integratietests dekken automatische `EndReason` + boundary-`ActualEndAt` en behoud van `ManualStop` + echte stoptijd.
- ✅ Test-cleanup hersteld; volledige CI groen op commit `b810214e`.
- ✅ Startup terminal recovery herbouwt ontbrekende/verouderde terminale Stop-work vóór de scheduler claim-loop wordt vrijgegeven.
- ✅ Recoverytests dekken ontbrekende overdue terminal work (direct claimbaar) en vervanging van obsolete pending terminal work.
- ✅ Volledige CI groen op commit `203d562a` (`fix: rebuild terminal work during recovery`).

## Fase C — provider identity/matching — SCHED-017, deel SCHED-009 ✅ AFGEROND

### C1 — centrale `ProviderActionMatchPolicy`

V1-besluit provider timestamp tolerance: **5 seconden**. Dit is bewust een **engineering margin**, geen gemeten 2Park-SLA. Start en End gebruiken dezelfde tolerance tenzij later live 2Park-bewijs een onderscheid rechtvaardigt.

Matchingprioriteit:

1. bekende provider action-id;
2. provider productcontext;
3. genormaliseerd kenteken;
4. semantisch geldige status;
5. Start/End binnen de centrale tolerance.

Aanvullende afspraken:

- Stop blijft primair action-id driven; geen onnodige fallback-identificatie toevoegen.
- Eerst beoordelen waarom `TwoParkProvider.StartActionAsync` nu `< 2 minuten` gebruikt voordat die logica wordt vervangen.
- `ExternalProviderAction` detection niet automatisch fallback-matchen wanneer het provider action-id onbekend is.
- Locationcode versus providerlabel is geen harde identity mismatch.
- Zonder bekend provider action-id mag fallback alleen één unieke kandidaat accepteren.

- ✅ Centrale `ProviderActionMatchPolicy` en gerichte unit tests toegevoegd; CI groen op commit `8ffaa755` (`fix: centralize provider action matching`).
- ✅ Directe start-readback en `StartVisitProviderReconciler` aangesloten op de centrale policy; CI groen op commit `417afe53` (`fix: apply provider match policy to starts`).
- ✅ Reconciler behoudt conservatieve execution-evidence-semantiek: plate/start-evidence met gewijzigde end/status wordt niet blind retryable.
- ✅ Extend precheck, directe extend-readback en `ContinueVisitProviderReconciler` aangesloten op dezelfde centrale action-id/product/status/timestamp-semantiek; CI groen op commit `08c7d831` (`fix: apply provider match policy to extensions`).
- ✅ Matchcriteria ondersteunen onbekende caller-context expliciet: kenteken/status/timestamps worden alleen toegepast wanneer de caller die informatie bezit; een bekende action-id valt nooit terug naar een andere kandidaat.
- ✅ Stop read-back en Stop reconciliation gebruiken dezelfde centrale action-id/product identity; Stop blijft strikt action-id driven zonder fallback; CI groen op commit `797021ec` (`fix: apply provider match policy to stops`).
- ✅ Beoordeeld waarom `TwoParkProvider.StartActionAsync` `< 2 minuten` gebruikte: de startresponse levert geen bruikbaar action-id op, waardoor de adapter via read-back een fallback-kandidaat moest zoeken; de 2-minutenwaarde was een heuristische zoekwindow, geen 2Park-SLA.
- ✅ `TwoParkProvider.StartActionAsync` gebruikt dezelfde unique-fallback policy met product, genormaliseerd kenteken, `active|scheduled` en 5-seconden Start/End-tolerance; CI groen op commit `7d176ec8` (`fix: align twopark start readback matching`).
- ✅ Actieve provider-action discrepancy-detectie gebruikt centrale action-id/product identity en de centrale 5-seconden End-tolerance; integratietests dekken ±4 seconden als gezond en +6 seconden als `ProviderActionEndMismatch`; CI groen na gerichte testfixes op commit `5cf172bc`.
- ✅ `ExternalProviderAction` detectie blijft bewust exact action-id gebaseerd; er is geen fallback-koppeling voor onbekende provider IDs toegevoegd.
- ✅ Startup scheduler-rebuild gebruikt centrale action-id/product identity en centrale 5-seconden End-tolerance; statusafhandeling blijft expliciet `stopped`/`active` zodat externe stops niet als ontbrekende action worden geïnterpreteerd; CI groen op commit `adf59937` (`fix: align scheduler recovery matching`).
- ✅ `ReconcileScheduledCancelAsync` blijft bewust product-scoped en strikt bekend-action-id + `stopped` status; omdat dit Stop-semantiek is en geen lokale timestamp/fallbackheuristiek bevat, is geen cosmetische policy-conversie nodig.
- ✅ `VisitSchedulerWorkProcessor` gebruikt voor scheduled wake-up, free-gap predecessorcheck en aansluitende continuation centrale action-id/product identity; de twee provider-End checks gebruiken de centrale 5-seconden tolerance; CI groen op commit `5dedf4ba` (`fix: align scheduler provider matching`).
- ✅ Initial-coverage duplicate-prevention gebruikt centrale productcontext, kenteken-normalisatie en 5-seconden Start-tolerance en blijft bewust een conservatieve `Any`-guard; CI groen op commit `ef04e4d3` (`fix: align initial coverage duplicate guard`).
- ✅ Overige strict known-action-id read-backs zijn geïnventariseerd en blijven bewust lokaal waar zij geen fallback/timestampheuristiek bevatten en semantische validatie al door de bovenliggende flow gebeurt.

## Fase D — JIT scheduled continuation — SCHED-001 — kern ✅ AFGEROND

### D1/D2 — future continuation en scheduled providerstart

- ✅ Directe start-readback accepteert naast `active` ook `scheduled` als geldige bevestigde providerstatus, met dezelfde centrale identity- en timestampcriteria; CI groen op commit `89fba047` (`fix: accept scheduled provider starts`).
- ✅ `ProviderContinuationStartStore.PrepareAttemptAsync` accepteert een nog actieve predecessor vóór diens eindgrens, zodat T-5 geen exception/retry-polling meer vereist; CI groen na gerichte compilefix op commit `e0bc330a`.
- ✅ Successor blijft `PlannedStartAt = predecessor.PlannedEndAt + 1 seconde`; de bestaande guard tegen een latere provideraction blijft de single-successor invariant bewaken.
- ✅ Scheduler-processor end-to-end coverage bewijst T-5: één future successor wordt als `scheduled` bevestigd en redundant schedulerwork maakt geen tweede successor; CI groen na gerichte test-isolatie/cleanupfixes op commit `2ebf464d`.
- ✅ Stop-pad end-to-end bewezen: de future `scheduled` successor en actieve predecessor worden beide veilig gestopt zonder open provideraction achter te laten; CI groen op commit `f581a135` (`test: cover stopping scheduled jit successor`).
- ✅ Bestaande continuation-recovery dekt Unknown/restart zonder tweede provideraction; gecombineerd met de JIT replay-test is duplicate-prevention voor de kernflow gedekt.
- ⏸️ Klokgestuurde `scheduled -> active` providertransitie en activation-boundary bewijs worden afgerond in fase G / SCHED-016, omdat TwoParkMock status nu alleen bij creatie bepaalt.

## Fase E — free-gap / overnight continuation — SCHED-002

### E1 — pre-schedule volgend betaald segment

- 🚧 Eerste slice: `VisitStartStore` plant een later betaald segment op T-5 van `nextPaid.Start` in plaats van exact op de betaalgrens. De overige schedulingpaden volgen na groene CI.

## Volgende hoofdfasen

- 🚧 Fase E — SCHED-002 free-gap / overnight continuation.
- 📋 Fase F — SCHED-009/010 algemene recovery hardening.
- 📋 Fase G — SCHED-016 TwoParkMock + boundary test harness; rondt ook SCHED-001 activationbewijs af.
- 📋 Fase H — regressieverificatie SCHED-001 t/m SCHED-012.
- 📋 Fase I — SCHED-018 observability als laatste.
