# Scheduler hardening — voortgang

**Laatst bijgewerkt:** 2 oktober 2026

## Eindstatus vóór SCHED-018

Scheduler-hardening en regressieverificatie voor **SCHED-001 t/m SCHED-017 zijn afgerond en CI-groen**. De enige bewust nog niet gestarte schedulerverbetering is **SCHED-018 — persistente scheduler observability/audit trail**.

## Fase A — state policy en locking ✅

- centrale `VisitSchedulerWorkExecutionPolicy` toegevoegd;
- claim, retry en recovery gebruiken dezelfde `Execute` / `Defer` / `Cancel` semantiek;
- `StopVisit` is niet afhankelijk van health;
- `LongVisitWarning` volgt lifecycle, niet providerhealth;
- uniforme Visit-first lock-order ingevoerd;
- race-tests voor schedulerclaim versus Stop/end-time change en gelijke `DueAt`-prioriteit groen.

Belangrijkste bewijs: `4023c6e3`.

## Fase B — terminale Visit lifecycle ✅

- `VisitEndReason` toegevoegd;
- centrale `VisitTerminalBoundaryCalculator`;
- tie-break `MaxPaid -> MaxElapsed -> DesiredEnd`;
- `VisitTerminalWorkPlanner` bewaakt duurzame terminale `StopVisit`-work;
- automatische finalization gebruikt terminale `DueAt` als functionele `ActualEndAt`;
- startup recovery herbouwt ontbrekende/verouderde terminal-work.

Belangrijkste eindcommit recovery: `203d562a`.

## Fase C — provider identity/matching ✅

V1 timestamp tolerance: **5 seconden**, bewust een engineering margin en geen gemeten 2Park-SLA.

Matchingprioriteit:

1. known action-id;
2. provider product;
3. normalized plate indien relevant;
4. semantische status;
5. Start/End binnen tolerance indien relevant.

Aanvullend:

- Stop blijft action-id driven;
- bekende-ID mismatch mag nooit fallbacken;
- onbekende-ID fallback vereist één unieke kandidaat;
- locationlabel versus code is geen identity mismatch;
- `ExternalProviderAction` detection blijft exact-ID gebaseerd.

Fase afgerond t/m `ef04e4d3`.

## Fase D — JIT scheduled continuation / SCHED-001 ✅

- T-5 maakt exact één future `scheduled` successor;
- contiguous start = predecessor.End + 1 seconde;
- direct read-back accepteert `scheduled`;
- redundant work/replay maakt geen duplicate;
- Stop ruimt active predecessor én scheduled successor op;
- activation boundary met bestuurbare mockklok bewezen.

Belangrijk bewijs: `18fc51cd` en `f581a135`.

## Fase E — free-gap / overnight / SCHED-002 ✅

- volgend betaald segment wordt voorbereid op `nextPaid.Start - 5 minuten`;
- geen providerdekking tijdens gratis tijd;
- successor na gratis gat start exact op `nextPaid.Start`;
- restart dupliceert scheduled future coverage niet.

Fase groen t/m `6cc9bf05`.

## Fase F — recovery hardening / SCHED-009 + SCHED-010 ✅

- V1 single-instance deploymentcontract vastgelegd;
- achtergelaten claimed schedulerwork wordt policygedreven hersteld;
- stale `InProgress` provideroperations pas na de bestaande 5-minuten attempt lease naar `Unknown`;
- interrupted `Reconciling` wordt via read-back hervat zonder repeat mutation.

Belangrijkste commits: `79ce2d04`, `d8f48c26`, `2de98742`, `0b17d16a`.

## Fase G — TwoParkMock boundary harness / SCHED-016 ✅

- centrale mockklok;
- set/advance/reset endpoints;
- dynamic `scheduled -> active` read-back;
- visibility delay klokgestuurd;
- Start/End read-back offsets;
- locationlabel override;
- expliciete post-End modi `keep-active`, `completed`, `hide`;
- providercapaciteit uit derived state;
- scheduled actions tellen alleen mee wanneer de test dat expliciet configureert.

G8 is groen op `110ebce5`. Daarmee is fase G als **harness/capabilityfase afgerond**. Exact live 2Park post-End gedrag en scheduled-capacity blijven externe observatiepunten, niet mock-defaults.

## Fase H — regressieverificatie SCHED-001 t/m SCHED-017 ✅

Scenario's en gedeelde bevindingen zijn opnieuw tegen de geharde implementatie gelopen.

- SCHED-003 manual Stop versus continuation ✅ — `2df05292`
- SCHED-004 shortening ✅ — `c958c725`
- SCHED-005 extension ✅ — `d8c20778`
- SCHED-006 rolling horizon ✅ — scheduler `TimeProvider` + twee-horizon regressietest t/m `befb270b`
- SCHED-007/008 duration boundaries ✅ — `ae18816f`
- SCHED-009/010 provider/restart recovery ✅ — `4f637f00`
- SCHED-011/012 discrepancy + Long Visit warning ✅ — `a4968334`
- SCHED-013 t/m 017 gedeelde hardening opnieuw geverifieerd ✅ — `f7aba92d`
- SCHED-001/002 detaildocumentatie wordt in de aansluitende documentatieronde gelijkgetrokken met het reeds groene implementatiebewijs.

## Documentatieronde vóór SCHED-018 🚧

Voor aanvang van SCHED-018 wordt eerst alle levende functionele en technische Visit/scheduler-documentatie gelijkgetrokken met de actuele implementatie:

- functionele Visit/schedulerregels;
- terminale lifecycle en eindredenen;
- T-5 JIT en free-gap semantics;
- provider matching/tolerance;
- Visit-first locking en work-type policy;
- recovery/single-instance contract;
- TwoParkMock boundary harness;
- auditstatus SCHED-001 t/m 017.

## Fase I — SCHED-018 observability 📋 NIET GESTART

SCHED-018 blijft bewust geparkeerd. Eerst wordt deze documentatieronde afgerond en beoordeeld. Er is nog geen observability-datamodel, eventcatalogus of implementatie gestart.
