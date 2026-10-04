# V1 implementatieplanning

De uitvoerbare V1-volgorde wordt primair bijgehouden in GitHub issue #92. Dit document geeft de hoofdfasen en de actuele schedulerstatus weer.

## Hoofdfasen

1. Technische baseline
2. Design foundation + app shell
3. Identiteit, sessies en beheerbasis
4. Policies, parkeerregels en tijdmodel
5. 2Park contract, mock en adapter
6. Kern-Visit lifecycle
7. Scheduler, continuation en recovery
8. Notificaties en PWA-integratie
9. Dashboards, historie en administratie
10. Echte 2Park-validatie en hardening
11. V1 release

De volgorde is dependency-driven en niet gebaseerd op issue-nummers. API, persistence, UI, tests en documentatie worden zoveel mogelijk samen opgeleverd.

## Actuele schedulerstatus — 4 oktober 2026

De reliability-hardening rond Visit lifecycle, scheduler, provider matching, locking, recovery en TwoParkMock is voor **SCHED-001 t/m SCHED-017 afgerond en opnieuw geverifieerd**.

Daarmee zijn onder andere gerealiseerd:

- terminale Visitgrenzen en eindredenen;
- JIT scheduled continuation en free-gap/overnight hervatting;
- Visit-first locking en work-type policy;
- centrale provider identity/timestamp matching;
- single-instance restart recovery;
- deterministische boundary test harness;
- regressieverificatie van de oorspronkelijke scheduler-scenario's.

**SCHED-018 scheduler observability:** afgerond en geverifieerd met 430/430 .NET-tests, 16/16 frontendtests en een productiebuild. De volledige migratieketen slaagt op de wegwerp-PostgreSQL-testdatabase; migratie naar een blijvende omgeving hoort bij deployment.

Zie `docs/technisch/visit-scheduler-audit/PROGRESS.md` voor de scheduler-hardeninghistorie en #92 voor de bredere V1-planning.
