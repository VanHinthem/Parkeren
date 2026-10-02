# SCHED-010 — crash/restart tijdens claimed work

**Status:** ✅ Opnieuw geverifieerd na recovery-hardening  
**Prioriteit:** middel/hoog

## Gewenste invariant

Een proces/container-restart mag geen schedulerwork permanent `Claimed` achterlaten en mag geen provider mutation dupliceren. Recovery moet eerst externe onzekerheid oplossen en daarna schedulerwork veilig hervatten.

## Huidig deploymentcontract

Voor V1 is het deploymentmodel expliciet vastgelegd als **single-instance** voor `Parkeren.Api` / `VisitSchedulerWorker`:

- exact één actieve scheduler-instance;
- geen applicatieschaal > 1;
- geen overlappende rolling deployment met twee gelijktijdig actieve workers;
- restart/update vervangt de bestaande instance.

Wanneer later horizontale schaal of overlappende deployments gewenst zijn, is een distributed lease/liveness-mechanisme nodig. Dat is bewust geen impliciete V1-aanname meer.

## Startup recovery

`VisitSchedulerWorker` opent de normale claim-loop pas nadat `VisitRecoveryService.RecoverAsync` succesvol is afgerond. Startup recovery wordt bij fouten opnieuw geprobeerd; nieuwe schedulerclaims lopen dus niet vooruit op onopgeloste recovery.

Achtergelaten `Claimed` schedulerwork wordt per Visit verwerkt onder dezelfde Visit-first lock-order als runtime claiming. Na de Visit advisory lock wordt de exacte scheduler-row `FOR UPDATE` herlezen en opnieuw beoordeeld met `VisitSchedulerWorkExecutionPolicy`:

- `Execute` → terug naar `Pending` op de oorspronkelijke/due tijd;
- `Defer` → terug naar `Pending` met de centrale tijdelijke defer-delay;
- `Cancel` → definitief geannuleerd.

Recovery reset dus niet langer blind alle claimed rows, maar gebruikt dezelfde lifecycle/health-semantiek als de normale scheduler.

## Provideroperations bij crash/restart

Persistente provideroperations beschermen externe mutations tegen duplicatie:

- recente `InProgress` attempts binnen de bestaande vijf-minuten lease blijven ongemoeid;
- stale `InProgress` attempts worden onder de Visit-lock naar `Unknown` gebracht;
- lokale Start/ContinueStart/Stop action-health wordt eveneens onzeker gemaakt waar nodig;
- Extend bewaart de bestaande actieve action-health maar maakt de operation zelf `Unknown`;
- de Visit gaat naar reconciliation en provider read-back bepaalt de uitkomst;
- interrupted `Reconciling` operations worden bij startup opnieuw als `Unknown` hervat;
- er vindt geen blind mutation-retry plaats.

Na reconciliation worden schedulerwork en terminal work opnieuw opgebouwd vanuit de actuele duurzame Visit/providerstate.

## Regressiebewijs

De huidige tests dekken gezamenlijk:

- claimed schedulerwork recovery via de centrale execution policy;
- Visit-first locking tijdens recovery;
- stale versus recente provider attempt lease;
- stale Start wordt Unknown in plaats van opnieuw gestart;
- stale Extend houdt lokale action-health intact maar wordt gereconcilieerd;
- interrupted reconciliation wordt hervat;
- persisted providerresponse kan na restart worden bevestigd zonder tweede mutation;
- Unknown continuation en Stop worden na restart gereconcilieerd zonder duplicate provideraction;
- missing/obsolete terminal work wordt vóór schedulerclaims herbouwd;
- recovery van scheduled successors maakt geen duplicate successor.

## Relaties

- [SCHED-009](SCHED-009.md): unknown provideroperations worden vóór retry gereconcilieerd.
- [SCHED-014](SCHED-014.md): recovery gebruikt dezelfde Visit-first lock-order.
- [SCHED-015](SCHED-015.md): recovery gebruikt dezelfde work-type execution policy.
- [SCHED-013](SCHED-013.md): terminal work wordt tijdens startup hersteld.

## Later multi-instance ontwerp

Het huidige gedrag is correct binnen het expliciete V1 single-instance contract. Multi-instance support is een aparte toekomstige capability en vereist minimaal een owner lease/livenessmodel voor schedulerclaims en een expliciet protocol voor recovery tijdens gelijktijdig levende workers.

## Conclusie

Het oorspronkelijke architectuurpunt is voor V1 opgelost. Het deploymentcontract is expliciet single-instance en startup recovery gebruikt consistente locking, work-type policy en provider reconciliation. Een restart laat claimed work niet permanent hangen en dupliceert unresolved provider mutations niet. Er resteert geen zelfstandig SCHED-010-gat binnen het vastgelegde V1 deploymentmodel.