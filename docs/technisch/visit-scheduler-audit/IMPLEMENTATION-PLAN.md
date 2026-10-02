# Visit scheduler — geconsolideerde implementatievolgorde

**Status:** gereed voor uitvoering  
**Datum:** 2 oktober 2026  
**Scope:** implementatie van SCHED-001 t/m SCHED-017; SCHED-018 volgt bewust als laatste observability-stap

Dit document zet de uitgewerkte technische ontwerpen om in één uitvoerbare volgorde. Doel is systeemafhankelijkheden te respecteren, dubbel werk te voorkomen en iedere wijziging klein genoeg te houden voor afzonderlijke CI-validatie.

## Hoofdregel

Geen brede scheduler-rewrite. We behouden de bestaande sterke bouwstenen:

- `VisitSchedulerWork` als durable work queue;
- `ProviderOperation` vóór externe provider-mutaties;
- Visit advisory locks;
- reconciliation vóór retry;
- bestaande Stop/finalization-flow;
- versioned parking rules en paid/free-segmentatie;
- startup recovery als gate vóór nieuwe schedulerclaims.

De implementatie wordt opgebouwd in lagen. Een hogere laag mag pas worden aangepast wanneer de fundamentele concurrency/state-invarianten eronder stabiel zijn.

## Implementatiefase A — fundament: state policy en locking

### A1. Work-type execution policy — SCHED-015

Introduceer `VisitSchedulerWorkExecutionPolicy` met uitkomsten:

- `Execute`;
- `Defer`;
- `Cancel`.

Eerst pure unit tests voor de volledige status/healthmatrix.

Daarna:

- `PostgresVisitSchedulerWorkClaimer` laten evalueren via deze policy;
- `ReleaseFailedAsync` dezelfde policy laten gebruiken;
- geen tijdelijke health-state meer permanent annuleren wanneer het work later weer relevant kan worden.

Waarom eerst: alle volgende terminal/continuation-work gebruikt dezelfde claimsemantiek.

### A2. Uniforme lock-order — SCHED-014

Globale regel:

```text
Visit advisory lock -> scheduler/provider rows
```

Schedulerclaim wordt tweefasen:

1. kandidaat due work identificeren zonder row lock vast te houden;
2. Visit advisory lock nemen;
3. exacte work-row `FOR UPDATE` locken;
4. work en Visit opnieuw valideren;
5. policy toepassen;
6. claim/defer/cancel persistent maken.

`ReleaseFailedAsync`, Stop en end-time flows moeten dezelfde lock-order respecteren.

Voeg gerichte PostgreSQL-concurrencytests toe voor:

- schedulerclaim versus manual Stop;
- schedulerclaim versus end-time change;
- twee workers op hetzelfde work;
- gelijke `DueAt` met prioriteit `StopVisit -> ContinueProviderCoverage -> LongVisitWarning`.

Waarom vóór lifecycle/providerfixes: we willen nieuwe durable work niet bouwen op de huidige lock-inversion.

## Implementatiefase B — terminale Visit lifecycle — SCHED-013, 007, 008

### B1. Domeinstate voor einde

Introduceer:

```text
VisitEndReason
- ManualStop
- DesiredEndReached
- MaxVisitElapsedDurationReached
- MaxPaidParkingDurationReached
```

`Visit.BeginStopping(reason)` legt reden atomair vast en de reden blijft immutable.

Voeg lifecycle-tests toe.

### B2. Centrale terminal boundary calculator

Introduceer één `VisitTerminalBoundaryCalculator` die de vroegste grens bepaalt uit:

- `DesiredEndAt`;
- `StartAt + MaxVisitElapsedDuration`;
- wall-clock boundary waarop `MaxPaidParkingDuration` is opgebruikt.

Hergebruik bestaande paid/free-segmentatie. Geen aparte betaalde-duurlogica in scheduler, recovery of end-time change.

Tie-break bij exact gelijke grens:

1. MaxPaid;
2. MaxElapsed;
3. DesiredEnd.

### B3. Durable terminal `StopVisit` work

Breid schedulerwork uit met optionele `EndReason` en introduceer idempotente `EnsureTerminalWorkAsync`.

Invariant:

```text
Active Visit + berekenbare terminal boundary
=> exact één relevante Pending/Claimed terminale StopVisit-taak
```

Aanroepen na:

- succesvolle Visit-start;
- `DesiredEndAt` wijziging;
- startup recovery.

Ook free-only Visits krijgen terminal work.

### B4. Terminal execution en recovery

Terminal `StopVisit` gebruikt de bestaande Stop/provider/finalization-flow.

Regels:

- manual Stop: `ActualEndAt = echt stopmoment`;
- automatische boundary: `ActualEndAt = berekende terminal boundary`, ook bij vertraagde worker/restart;
- geen completion zolang provideractions nog niet terminal zijn;
- geen continuation meer op of na terminal boundary.

Recovery moet ontbrekend/verouderd terminal work herstellen en overdue terminal work direct uitvoerbaar maken.

### B5. Verificatie

Minimaal:

- finite paid Visit;
- volledig free-only Visit;
- paid -> free tail;
- overnight Visit;
- MaxElapsed;
- MaxPaid;
- restart voorbij boundary;
- manual Stop versus terminal work;
- capaciteit komt vrij bij `Completed`.

## Implementatiefase C — provider identity/matching — SCHED-017, deel SCHED-009

### C1. Centrale `ProviderActionMatchPolicy`

Alle losse exacte timestampchecks vervangen door één policy.

Prioriteit:

1. provider action-id wanneer bekend;
2. productcontext;
3. genormaliseerd kenteken;
4. semantisch geldige status;
5. timestamps binnen één centrale tolerantie.

Locatiecode versus providerlabel is geen harde identity mismatch.

Fallback zonder provider action-id mag alleen een **unieke** kandidaat accepteren.

### C2. Concrete tolerance vastleggen

Vóór deze codecommit moet één concrete timestamp-tolerantie worden gekozen. De gekozen waarde wordt centraal configureerbaar/constant gehouden en niet verspreid hardcoded.

### C3. Start/read-back en reconciler aansluiten

- directe start-readback gebruikt de match policy;
- reconciler gebruikt dezelfde policy;
- periodic discrepancy detection gebruikt dezelfde tolerance/semantiek;
- geen 1 ms/equality checks meer voor providertimestamps.

Waarom vóór JIT: future `scheduled` continuation kan pas betrouwbaar als identity/reconciliation niet meer foutief Unknown oplevert.

## Implementatiefase D — JIT scheduled continuation — SCHED-001

### D1. Continuation schedule contract

Voor directe aansluiting:

```text
work due = predecessor.PlannedEndAt - 5 minuten
successor.PlannedStartAt = predecessor.PlannedEndAt + 1 seconde
```

Maximaal één scheduled successor per Visit.

De continuation store mag vanaf het JIT-venster voorbereiden; de huidige eis dat predecessor al geëindigd moet zijn vervalt.

### D2. `scheduled` als succesvolle providerstart

Een bevestigde future action met provider action-id en status `scheduled` betekent:

- `ProviderOperation = Succeeded`;
- lokale action blijft `Scheduled`;
- geen Unknown/retry alleen omdat hij nog niet active is.

### D3. Scheduled activation reconciliation

Introduceer één expliciet pad voor:

```text
Scheduled -> Active
```

rond/na `PlannedStartAt`.

Binnen een korte activation-grace is remote `scheduled` nog geldig; na grace volgt reconciliation/attention in plaats van blind nieuwe mutation.

De concrete grace wordt vóór implementatie vastgelegd op basis van worker-cadans en providerlatency.

### D4. Terminal guard

Vlak vóór iedere provider continuation mutation opnieuw controleren:

- Visit nog geschikt volgens SCHED-015;
- terminal boundary niet bereikt/gepasseerd;
- geen bestaande scheduled successor;
- geen unresolved operation.

## Implementatiefase E — free-gap / overnight continuation — SCHED-002

Na een gratis interval wordt de volgende paid action eveneens vooraf als scheduled action aangemaakt:

```text
work due = nextPaid.Start - 5 minuten
PlannedStartAt = nextPaid.Start
```

De `+1 seconde` regel geldt alleen voor direct aansluitende provideractions, niet na een echt gratis gat.

De flow mag niet eisen dat een oude predecessor na natuurlijke expiry nog remote `active` is.

Post-End providerstatus blijft providercontract-afhankelijk; geen aannames hardcoden.

Verificatie:

- overnight blijft Visit `Active` tijdens gratis tijd;
- geen provideraction tijdens gratis tijd;
- successor wordt T-5 gepland;
- successor start exact op nieuwe paid boundary;
- restart tijdens gratis periode dupliceert niets.

## Implementatiefase F — recovery hardening — SCHED-009 + SCHED-010

V1 deploymentcontract:

```text
exact één actieve Parkeren.Api / VisitSchedulerWorker instance
```

Daarom geen distributed lease/heartbeat in V1.

Startupvolgorde:

1. claims herstellen;
2. unresolved provideroperations reconciliëren;
3. scheduled/active provideractions inventariseren;
4. terminal work herstellen;
5. continuation work herstellen;
6. pas daarna normale schedulerclaim-loop starten.

Belangrijk:

- bestaande `Scheduled` successor nooit dupliceren;
- `Unknown` nooit blind opnieuw muteren;
- `AttentionRequired` blokkeert continuation maar niet terminal Stop;
- alle claim-recovery gebruikt SCHED-014 lock-order en SCHED-015 state policy.

Multi-instance/rolling deployment wordt expliciet buiten V1 gehouden en vereist later een lease/liveness-ontwerp.

## Implementatiefase G — TwoParkMock en boundary test harness — SCHED-016

### G1. Deterministische testklok

TwoParkMock krijgt een testklok zodat tests tijd kunnen vooruitzetten zonder echte minuten te wachten.

Live bevestigd gedrag mag automatisch worden gemodelleerd:

```text
now < Start     => scheduled
Start <= now < End => active
```

Expliciet gestopte/geannuleerde actions blijven terminal.

### G2. Post-End gedrag niet verzinnen

Tot echte 2Park-tests post-End gedrag bevestigen:

- geen vaste productieachtige post-End status aannemen;
- testmodus/fixture mag expliciet een scenario instellen;
- tests die provider-expiry bewijzen moeten duidelijk aangeven welk providercontract ze simuleren.

### G3. Testbare providerafwijkingen

Mock ondersteunt gericht:

- visibility delay;
- timestamp offsets;
- locatiecode/labelverschil;
- unknown outcomes/timeouts;
- capacity mode voor scheduled actions;
- forced status/read-back scenario's waar nodig.

### G4. Integrale boundary-tests

Minimaal end-to-end:

- T-5 future successor;
- scheduled -> active;
- direct +1s adjacency;
- overnight/free-gap;
- timestamp tolerance;
- unknown/reconciliation;
- restart met scheduled successor;
- terminal boundary versus continuation;
- manual Stop/shortening versus scheduled successor;
- PostgreSQL lock races.

## Implementatiefase H — scenario-regressie SCHED-001 t/m SCHED-012

Na de systeemwijzigingen ieder oorspronkelijk auditscenario opnieuw doorlopen.

Per SCHED-document:

- actual codeflow controleren;
- tests koppelen;
- resterende open providerpunten expliciet houden;
- status alleen naar ✅ wanneer bewijs daadwerkelijk aanwezig is.

Bijzondere aandacht:

- SCHED-003 manual Stop vs continuation;
- SCHED-004 shortening;
- SCHED-005 extension;
- SCHED-006 rolling horizon;
- SCHED-011 discrepancy/recovery;
- SCHED-012 Long Visit warning.

## Implementatiefase I — observability als laatste — SCHED-018

Pas nadat SCHED-001 t/m SCHED-017 stabiel zijn:

1. samen definitieve eventcatalogus bepalen;
2. persistent scheduler/audit eventmodel toevoegen;
3. belangrijke schedulerbesluiten per Visit vastleggen;
4. beheer-timeline/API/UI toevoegen waar gewenst.

Geen debug-logspiegel maken. Alleen betekenisvolle state-/beslisevents vastleggen.

## Voorgestelde commit- en CI-slices

Iedere regel hieronder is een logisch stopmoment. Na iedere commit CI controleren vóór de volgende stap.

1. `fix: add scheduler work execution policy`
2. `fix: enforce visit-first scheduler locking`
3. `feat: add visit end reason`
4. `feat: calculate visit terminal boundary`
5. `feat: schedule terminal visit work`
6. `fix: finalize visits at terminal boundary`
7. `fix: rebuild terminal work during recovery`
8. `fix: centralize provider action matching`
9. `fix: accept scheduled provider starts`
10. `fix: reconcile scheduled provider activation`
11. `fix: schedule jit provider continuation`
12. `fix: schedule continuation before paid resume`
13. `fix: harden scheduler startup recovery`
14. `test: add deterministic twopark mock clock`
15. `test: add scheduler boundary scenarios`
16. `test: verify scheduler audit scenarios`
17. later: `feat: add visit scheduler audit trail`

Sommige slices kunnen tijdens uitvoering iets kleiner worden als een wijziging anders te breed wordt. Ze mogen niet groter worden enkel om sneller door de lijst heen te gaan.

## Nog te kiezen vóór specifieke implementaties

Deze punten zijn bewust niet verzonnen en moeten op het juiste moment expliciet worden besloten:

1. provider timestamp tolerance — vóór fase C;
2. scheduled activation grace — vóór fase D;
3. echte 2Park post-End status/visibility — vóór een realistische vaste mocksemantiek daarvoor;
4. of scheduled actions meetellen voor provider-capaciteit — vóór definitieve capacitytests;
5. concrete defer-delay voor tijdelijk niet-uitvoerbaar schedulerwork — tijdens fase A;
6. eventueel PostgreSQL `40P01` retry-aantal/backoff als defensieve laag — tijdens fase A2.

Deze open operationele parameters blokkeren de architectuur niet, maar worden nooit lokaal met verschillende waarden hardcoded.

## Definition of Done scheduler hardening

De scheduler-hardening is voor V1 pas gereed wanneer:

- iedere finite Visit een betrouwbare terminale lifecycle heeft;
- geen provideraction functioneel een Completed Visit kan overleven;
- continuation vóór providergrenzen JIT kan worden aangemaakt en `scheduled` correct wordt verwerkt;
- free/overnight coverage zonder gaten wordt hervat;
- provider timestamps niet tot foutieve Unknown/discrepancy leiden;
- timeout/restart geen duplicate provider mutation veroorzaakt;
- manual Stop/end-time change/schedulerclaim geen lock-order deadlocks opleveren;
- work-type state/healthgedrag expliciet en getest is;
- recovery terminal en continuation work volledig kan reconstrueren;
- TwoParkMock de bewezen providerstates deterministisch kan simuleren;
- SCHED-001 t/m SCHED-012 opnieuw met tests zijn geverifieerd;
- resterende echte-provider-onzekerheden expliciet in #71 staan en niet door de mock of code worden verborgen.

Daarna volgt SCHED-018 als afzonderlijke observabilityverbetering.