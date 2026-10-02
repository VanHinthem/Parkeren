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

## Fase B — terminale Visit lifecycle

### B1 — SCHED-013 Visit end reason

- 🚧 `VisitEndReason` toegevoegd met `ManualStop`, `DesiredEndReached`, `MaxVisitElapsedDurationReached` en `MaxPaidParkingDurationReached`.
- 🚧 `Visit.BeginStopping(reason)` legt de reden persistent vast; bestaande parameterloze `BeginStopping()` blijft compatibel en betekent `ManualStop`.
- 🚧 Domeintests dekken expliciete reden en immutable lifecycle-semantiek.
- 📋 Na groen: centrale `VisitTerminalBoundaryCalculator`.

## Volgende hoofdfasen

- 📋 Fase C — SCHED-017/009 provider identity/matching.
- 📋 Fase D — SCHED-001 JIT scheduled continuation.
- 📋 Fase E — SCHED-002 free-gap / overnight continuation.
- 📋 Fase F — SCHED-009/010 recovery hardening.
- 📋 Fase G — SCHED-016 TwoParkMock + boundary test harness.
- 📋 Fase H — regressieverificatie SCHED-001 t/m SCHED-012.
- 📋 Fase I — SCHED-018 observability als laatste.
