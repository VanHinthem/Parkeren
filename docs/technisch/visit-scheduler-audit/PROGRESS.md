# Scheduler hardening — voortgang

**Laatst bijgewerkt:** 2 oktober 2026

Dit bestand wordt vanaf de implementatiefase bijgewerkt in dezelfde logische commits als de codewijzigingen. Er worden geen aparte voortgangscommits gemaakt.

## Fase A — state policy en locking

### A1 — SCHED-015 work-type execution policy

- ✅ `VisitSchedulerWorkExecutionPolicy` toegevoegd.
- ✅ Volledige status/health-matrix unit-getest, inclusief `StopFailed`.
- ✅ CI groen op commit `fa697924` (`test: cover scheduler work execution policy`).
- 🚧 `PostgresVisitSchedulerWorkClaimer` aangesloten op `Execute` / `Defer` / `Cancel` in de huidige commit.
- 🚧 `Pending` work kan nu semantisch worden uitgesteld zonder kunstmatig claim/release-pad.
- 📋 `ReleaseFailedAsync` moet hierna dezelfde policy gebruiken.
- 📋 Integratie-/regressietests voor claimer/retry volgen binnen A1/A2.

Besluit defer-delay voor V1: tijdelijk niet-uitvoerbaar schedulerwork wordt standaard **1 minuut** uitgesteld. Recovery mag eerder herbeoordelen. De delay staat op één plek in de claimer en kan later eenvoudig configureerbaar worden gemaakt als operationele tuning dat nodig maakt.

### A2 — SCHED-014 uniforme lock-order

- 📋 Nog te implementeren.
- Doel: `Visit advisory lock -> scheduler/provider rows`.
- Daarna gerichte PostgreSQL concurrencytests.

## Volgende hoofdfasen

- 📋 Fase B — SCHED-013/007/008 terminale Visit lifecycle.
- 📋 Fase C — SCHED-017/009 provider identity/matching.
- 📋 Fase D — SCHED-001 JIT scheduled continuation.
- 📋 Fase E — SCHED-002 free-gap / overnight continuation.
- 📋 Fase F — SCHED-009/010 recovery hardening.
- 📋 Fase G — SCHED-016 TwoParkMock + boundary test harness.
- 📋 Fase H — regressieverificatie SCHED-001 t/m SCHED-012.
- 📋 Fase I — SCHED-018 observability als laatste.
