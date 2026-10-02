# SCHED-014 — lock-order inversion tussen schedulerclaim en Visit-mutaties

**Status:** ✅ Opgelost en regressiegedekt  
**Prioriteit:** hoog

## Gewenste invariant

Transacties die dezelfde Visit en schedulerwork combineren nemen locks in één consistente volgorde, zodat Stop, end-time changes, schedulerclaims en retries niet via lock-order inversion kunnen deadlocken.

## Huidig as-built gedrag

De globale regel is nu **Visit-first**:

1. selecteer alleen een kandidaat schedulerwork zonder eigenaarschap af te leiden;
2. neem `pg_advisory_xact_lock(VisitId)`;
3. neem daarna de exacte scheduler-row `FOR UPDATE`;
4. revalideer due/status/policy onder beide locks;
5. claim, defer of cancel pas daarna.

`PostgresVisitSchedulerWorkClaimer.ClaimNextDueAsync` gebruikt hiervoor een tweefasenclaim. De oorspronkelijke richting `work-row -> Visit-lock` bestaat daar niet meer.

`ReleaseFailedAsync` gebruikt dezelfde Visit-first volgorde en beoordeelt het work na de row lock opnieuw via de centrale execution policy.

Stop, end-time changes en andere Visit-mutaties waren al Visit-first en sluiten daardoor nu aan op dezelfde lock-order.

Bij gelijke `DueAt` geldt bovendien een expliciete selectieprioriteit:

`StopVisit -> ContinueProviderCoverage -> LongVisitWarning`.

## Regressiebewijs

PostgreSQL-concurrencytests dekken gericht:

- twee workers die hetzelfde due work proberen te claimen;
- schedulerclaim versus manual Stop;
- schedulerclaim versus end-time change;
- gelijke `DueAt` waarbij Stop semantisch wint;
- geen duplicate claim/mutation en geen verloren schedulerwork.

De runtime retry/release-flow gebruikt dezelfde lock-order als claiming, zodat een exception-pad de oorspronkelijke inversie niet opnieuw introduceert.

## Deploymentgrens

V1 draait expliciet single-instance. De lock-order is desondanks multi-worker-safe op transactieniveau; horizontaal schalen vereist aanvullend het lease/liveness-contract uit SCHED-010 en is geen onderdeel van deze fix.

## Conclusie

De bevestigde lock-order inversion is verwijderd. Schedulerclaim, retry/release en Visit-mutaties volgen nu dezelfde Visit-first lockdiscipline en worden door gerichte PostgreSQL-racetests bewaakt. Er resteert geen zelfstandig SCHED-014-gat.