# SCHED-015 — work-type state/health policy

**Status:** technisch ontwerp  
**Prioriteit:** hoog  
**Ontwerpdatum:** 2 oktober 2026  
**Raakt:** `ContinueProviderCoverage`, `StopVisit`, `LongVisitWarning`, recovery/retry en toekomstige scheduler-worktypes.

## Doel

De scheduler mag niet langer één generieke `VisitStatus.Active && VisitHealth.Healthy`-regel toepassen op ieder work-type.

Claimability, retry en terminal gedrag worden voortaan per work-type expliciet bepaald. Dezelfde policy moet door claimer, processor en retry/release-logica worden gebruikt zodat er geen onbereikbare of tegenstrijdige branches ontstaan.

## Ontwerpprincipe

Introduceer één centrale policy, conceptueel bijvoorbeeld:

```text
VisitSchedulerWorkExecutionPolicy.Evaluate(workType, visitStatus, visitHealth, now)
    -> Execute
    -> Defer
    -> Cancel
```

De policy bevat geen provider-I/O en muteert geen entiteiten. Hij geeft uitsluitend aan wat de scheduler met het workitem moet doen.

Daarmee verdwijnt verspreide state/health-logica uit:

- `PostgresVisitSchedulerWorkClaimer`;
- `ReleaseFailedAsync`;
- work-type-specifieke vroege processorbranches waar dezelfde semantiek al eerder door de claimer wordt beslist.

## Besloten matrix

### `ContinueProviderCoverage`

Doel: nieuwe providerdekking toevoegen of opvolgen.

| Visit state | Health | Besluit | Reden |
| --- | --- | --- | --- |
| `Active` | `Healthy` | `Execute` | normale continuation |
| `Active` | `Reconciling` | `Defer` | eerst bestaande onzekerheid oplossen; geen nieuwe mutation |
| `Active` | `AttentionRequired` | `Defer` | automatische provideruitbreiding blokkeren zonder work definitief kwijt te raken |
| `Stopping` | iedere | `Cancel` | lifecycle wordt beëindigd; geen nieuwe dekking meer |
| `Completed` / `Cancelled` | iedere | `Cancel` | terminale Visit |
| `Starting` | iedere | `Defer` | startflow is nog eigenaar van initiële dekking |

Belangrijk: `Reconciling` en `AttentionRequired` leiden dus niet meer automatisch tot permanent `Cancelled`. Het work blijft durable zodat recovery/admin-herstel de Visit weer uitvoerbaar kan maken zonder ontbrekende continuation opnieuw te moeten reconstrueren.

`Defer` krijgt een begrensde nieuwe `DueAt`; de exacte standaard-delay hoort bij implementatie, niet bij dit functionele contract. Recovery mag een eerdere herbeoordeling afdwingen.

### `StopVisit`

Doel: Visit veilig naar `Stopping -> Completed` brengen en provideractions beëindigen/annuleren/reconciliëren.

| Visit state | Health | Besluit | Reden |
| --- | --- | --- | --- |
| `Active` | `Healthy` | `Execute` | normale automatische/deferred stop |
| `Active` | `Reconciling` | `Execute` | onzeker providerwerk is juist onderdeel van veilige Stop-orchestration |
| `Active` | `AttentionRequired` | `Execute` | provider health mag een harde Visit-eindgrens niet neutraliseren |
| `Stopping` | iedere | `Execute` | Stop is idempotent/replayable; bestaande Stop-flow hervatten |
| `Completed` / `Cancelled` | iedere | `Cancel`/no-op | Visit is al terminal |
| `Starting` | iedere | `Execute` wanneer Stop-flow dit ondersteunt | handmatige/automatische terminal intent moet kunnen winnen van start; bestaande `BeginStopping` ondersteunt `Starting` |

Kerninvariant:

> `StopVisit` wordt nooit gecanceld uitsluitend omdat `VisitHealth != Healthy`.

Als providerstate onzeker is, blijft de Visit `Stopping` en gebruikt de bestaande reconciliation-flow. Het work mag opnieuw worden uitgevoerd of opgevolgd totdat finalization veilig mogelijk is.

### `LongVisitWarning`

Doel: gebruiker/beheerder waarschuwen dat een Visit nog actief is; dit is observability/UX, geen provider-mutatie.

| Visit state | Health | Besluit | Reden |
| --- | --- | --- | --- |
| `Active` | iedere health | `Execute` | de Visit is functioneel nog actief; provider-health verandert de looptijd niet |
| `Starting` | iedere | `Defer` | nog geen actieve Visit |
| `Stopping` | iedere | `Cancel` | Visit wordt al beëindigd |
| `Completed` / `Cancelled` | iedere | `Cancel` | terminal |

Daarmee blijft een `AttentionRequired` Visit zichtbaar als langdurig actief. Een attention-notificatie vervangt de Long Visit warning niet automatisch; het zijn verschillende signalen.

## `Execute`, `Defer`, `Cancel`

### Execute

Het work wordt geclaimd en door de processor uitgevoerd.

### Defer

Het work blijft bestaan en wordt naar een latere `DueAt` gezet. Dit is bedoeld voor een **tijdelijke of herstelbare** toestand.

Voorbeelden:

- continuation terwijl Visit `Reconciling` is;
- continuation terwijl Visit `AttentionRequired` is en later door recovery/admin kan worden vrijgegeven;
- Long Visit warning tijdens `Starting`.

Een defer mag geen provider mutation uitvoeren.

### Cancel

Alleen gebruiken wanneer het doel van het work definitief niet meer relevant kan worden.

Voorbeelden:

- continuation op een `Stopping`/`Completed` Visit;
- warning op een terminale Visit;
- Stop-work voor een al Completed/Cancelled Visit.

Daarmee wordt `Cancelled` een semantisch terminal schedulerbesluit, niet een generieke reactie op tijdelijke health.

## Claim en retry moeten dezelfde policy gebruiken

Dezelfde evaluator wordt gebruikt bij:

1. eerste claim van due work;
2. `ReleaseFailedAsync` na processor-exception;
3. recovery van eerder `Claimed` work;
4. eventueel expliciete scheduler-rebuild.

Dit voorkomt de huidige situatie waarin een processorbranch iets zou kunnen uitvoeren, maar het work door de claimer of retrylaag eerder permanent wordt geannuleerd.

## Relatie met SCHED-013

`StopVisit` is ook het terminal work-type voor natuurlijke Visit-finalization.

Daarom geldt op of na de effectieve terminale Visitgrens:

- `StopVisit` blijft uitvoerbaar ongeacht `Reconciling`/`AttentionRequired`;
- `ContinueProviderCoverage` mag niet meer uitvoeren;
- `LongVisitWarning` mag niet meer worden gecreëerd zodra de Visit naar `Stopping` gaat.

Bij gelijke `DueAt` moet Stop semantisch winnen. De concrete query/locking-order wordt samen met SCHED-014 geïmplementeerd.

## Relatie met provider discrepancy

`AttentionRequired` betekent voor continuation: **geen nieuwe providerdekking muteren**.

Het betekent niet:

- laat een provideraction onbeperkt doorlopen;
- negeer een harde Visit-eindtijd;
- verwijder durable terminal intent.

Daarom blijft Stop uitvoerbaar en continuation deferred.

## Relatie met handmatige Stop

Handmatige Stop gebruikt dezelfde Stop-orchestration en Visit advisory lock als scheduler Stop.

Wanneer handmatige Stop wint:

- Visit gaat naar `Stopping`;
- pending continuation/warning work wordt geannuleerd volgens de bestaande Stop-flow;
- eventueel later geclaimd terminal Stop-work moet idempotent/no-op kunnen eindigen.

Wanneer scheduler Stop wint, ziet een gelijktijdige handmatige Stop dezelfde `Stopping`/bestaande Stop-operation en sluit daarop aan.

## Recovery

Recovery beoordeelt schedulerwork opnieuw via dezelfde policy.

Voorbeelden:

- `ContinueProviderCoverage + Reconciling` blijft deferred;
- na succesvolle reconciliation en `Health = Healthy` wordt hetzelfde work weer uitvoerbaar;
- `StopVisit + AttentionRequired` blijft direct uitvoerbaar;
- terminal Visits houden geen relevant pending work over.

Recovery mag dus niet impliciet `AttentionRequired` als terminale schedulerstate behandelen.

## Ontwerpkeuze rond `AttentionRequired`

Voor V1 kiezen we **Defer**, niet permanent Cancel, voor continuation bij `AttentionRequired`.

Reden:

- de Visit is nog functioneel `Active`;
- de oorzaak kan door reconciliation of beheer worden opgelost;
- durable intent behouden is veiliger dan later moeten reconstrueren of vergeten dat dekking nog nodig was;
- zolang health niet herstelt, wordt geen nieuwe provider mutation uitgevoerd.

Dit vereist later wel dat herstel van `AttentionRequired -> Healthy` een duidelijk ondersteund pad heeft. Dat raakt SCHED-011/recovery, maar hoeft niet in deze wijziging volledig opnieuw ontworpen te worden.

## Geen aparte healthregel in processors

Processors mogen nog steeds work-specifieke actuele invarianten controleren, maar niet opnieuw een afwijkende generieke state/healthmatrix implementeren.

Voorbeeld:

- continuation processor mag controleren dat er geen terminal boundary is gepasseerd en dat provideroperation-state klopt;
- Stop processor mag controleren of provideractions nog afhandeling nodig hebben;
- warning processor mag payload/settings bepalen.

De vraag **of dit work-type in deze Visit state/health überhaupt schedulerbaar is** hoort bij één policy.

## Testmatrix

Minimaal geautomatiseerd bewijzen:

### ContinueProviderCoverage

- Active + Healthy -> Execute;
- Active + Reconciling -> Defer, niet Cancel;
- Active + AttentionRequired -> Defer, niet Cancel;
- Stopping -> Cancel;
- Completed/Cancelled -> Cancel;
- na Defer en health-herstel kan hetzelfde work alsnog worden geclaimd.

### StopVisit

- Active + Healthy -> Execute;
- Active + Reconciling -> Execute;
- Active + AttentionRequired -> Execute;
- Stopping -> replay/Execute;
- Completed/Cancelled -> no-op/Cancel;
- provider uncertainty houdt Visit in Stopping en vernietigt terminal work niet.

### LongVisitWarning

- Active + Healthy -> Execute;
- Active + Reconciling -> Execute;
- Active + AttentionRequired -> Execute;
- Stopping/Completed/Cancelled -> Cancel;
- Starting -> Defer.

### Retry

Voor ieder work-type moet `ReleaseFailedAsync` dezelfde matrix volgen als eerste claim.

## Implementatievolgorde

Na goedkeuring van het totale schedulerontwerp:

1. pure `VisitSchedulerWorkExecutionPolicy` + unit tests;
2. claimer laten evalueren via policy;
3. `ReleaseFailedAsync` laten evalueren via dezelfde policy;
4. processorbranches opschonen die alleen de oude generieke gate compenseren;
5. integratietests voor de volledige state/healthmatrix;
6. regressietests met SCHED-013 terminal work en SCHED-003 handmatige Stop races.

## Nog open

1. Welke standaard defer-delay gebruiken we per work-type? Dit is operationele tuning en kan bijvoorbeeld centraal configureerbaar worden, maar mag de semantiek hierboven niet wijzigen.
2. Het exacte herstelpad van `AttentionRequired -> Healthy` wordt met SCHED-011/recovery aangescherpt.
3. De concrete claim-order bij gelijke `DueAt` wordt samen met SCHED-014 vastgelegd.

Deze open punten blokkeren de state/healthmatrix zelf niet.