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
- 📋 Integratie-/regressietests voor claimer/retry volgen binnen A1/A2.

Besluit defer-delay voor V1: tijdelijk niet-uitvoerbaar schedulerwork wordt standaard **1 minuut** uitgesteld. Recovery mag eerder herbeoordelen. De delay staat op één plek in de claimer en kan later eenvoudig configureerbaar worden gemaakt als operationele tuning dat nodig maakt.

### A2 — SCHED-014 uniforme lock-order

- 🚧 `ClaimNextDueAsync` gebruikt in de huidige commit de tweefasenclaim: kandidaat zonder row lock, daarna Visit advisory lock, daarna exacte scheduler-row `FOR UPDATE` en her-validatie.
- 🚧 Bij gelijke `DueAt` geldt selectieprioriteit `StopVisit -> ContinueProviderCoverage -> LongVisitWarning`.
- 📋 `ReleaseFailedAsync` moet hierna dezelfde Visit-first lock-order krijgen.
- 📋 Daarna gerichte PostgreSQL concurrencytests.

## Volgende hoofdfasen

- 📋 Fase B — SCHED-013/007/008 terminale Visit lifecycle.
- 📋 Fase C — SCHED-017/009 provider identity/matching.
- 📋 Fase D — SCHED-001 JIT scheduled continuation.
- 📋 Fase E — SCHED-002 free-gap / overnight continuation.
- 📋 Fase F — SCHED-009/010 recovery hardening.
- 📋 Fase G — SCHED-016 TwoParkMock + boundary test harness.
- 📋 Fase H — regressieverificatie SCHED-001 t/m SCHED-012.
- 📋 Fase I — SCHED-018 observability als laatste.
