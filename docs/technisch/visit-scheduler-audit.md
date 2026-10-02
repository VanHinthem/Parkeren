# Visit scheduler — betrouwbaarheidsaudit

**Status:** in uitvoering  
**Start audit:** 2 oktober 2026

Dit document toetst de actuele schedulerimplementatie op `main` scenario voor scenario. De functionele en technische schedulerdocumentatie beschrijven wat het systeem doet; dit document beoordeelt vervolgens of de implementatie de gewenste invarianten betrouwbaar afdwingt.

Er wordt tijdens de audit niet stilzwijgend gerefactord. Eerst wordt gedrag bewezen, testdekking vastgesteld en een bevinding vastgelegd. Pas daarna wordt besloten of de implementatie moet wijzigen.

## Auditmethode

Per scenario beoordelen we:

1. gewenste functionele invariant;
2. daadwerkelijke codeflow;
3. persistente state en locks;
4. providergrens en idempotency;
5. gedrag bij fout/crash/restart;
6. bestaande geautomatiseerde tests;
7. ontbrekende testdekking;
8. conclusie en eventuele vervolgactie.

## Scenario 1 — betaald parkeren met opvolgende provideraction

### Scope

Baseline-flow:

```text
Visit starten tijdens betaald parkeren
-> eerste provideraction
-> Visit Active
-> ContinueProviderCoverage work op T-5
-> scheduler claimt work
-> providerstatus controleren
-> opvolgende provideraction
-> nieuw continuation-work indien nog meer dekking nodig is
```

Voor Oss geldt:

```text
Continuation = StartNewAction
MaxProviderActionDuration = 4 uur
```

### Gewenste invarianten

1. Een geldige actieve Visit behoudt providerdekking gedurende ieder betaald segment zolang `DesiredEndAt` en policygrenzen dat vereisen.
2. Er ontstaat nooit ongecontroleerde overlap tussen provideractions.
3. Een opvolgaction wordt niet dubbel aangemaakt bij retries of restart.
4. Een onzekere provideruitkomst wordt gereconcilieerd voordat dezelfde mutatie opnieuw wordt uitgevoerd.
5. Stop/end-time-mutaties kunnen niet ongecontroleerd racen met continuation.
6. T-5 heeft een duidelijke functionele betekenis en mag niet afhankelijk zijn van toevallige worker-timing rond de providergrens.

## As-built flow

### 1. Eerste provideraction

`StartVisitFlow` bepaalt of bij start direct providerdekking nodig is. Bij betaald parkeren:

- wordt de providercontext server-side aangeleverd;
- bepaalt `ProviderActionStartPlanner` de eerste action-eindtijd;
- wordt provider readiness gecontroleerd;
- wordt een persistente Start `ProviderOperation` plus `ProviderParkingAction` voorbereid;
- voert `StartVisitProviderExecutor` de provider-call uit;
- wordt de Visit na bevestigde start `Active`.

De startflow is daarmee persistent/idempotent opgezet en koppelt providerstate aan lokale Visit/action-state.

### 2. Planning continuation-work

Na een bevestigde providerstart berekent `ProviderStartResultStore` of na de eerste provideraction nog betaald parkeren resteert.

Bij aaneengesloten betaalde dekking wordt gepland:

```text
DueAt = ProviderCoverageSchedule.PrecheckAt(action.PlannedEndAt)
      = action.PlannedEndAt - 5 minuten
```

Bestaande integratietestdekking controleert expliciet dat `ContinueProviderCoverage` als `Pending` work op T-5 wordt opgeslagen.

### 3. Claiming

Wanneer `DueAt` is bereikt:

- `PostgresVisitSchedulerWorkClaimer` selecteert één due `Pending` item via `FOR UPDATE SKIP LOCKED`;
- daarna wordt de transactionele Visit advisory lock verkregen;
- Visit-status en health worden opnieuw gelezen;
- alleen `Active + Healthy` wordt geclaimd;
- daarna wordt het item `Claimed`.

Dit beschermt tegen dubbel claimen tussen workerinstanties en serialiseert schedulerclaims met Visit-mutaties die dezelfde advisory lock gebruiken.

### 4. Processor op T-5

`VisitSchedulerWorkProcessor`:

1. leest de actuele/scheduled provideraction;
2. berekent opnieuw `PrecheckAt(latestAction.PlannedEndAt)`;
3. controleert de functionele eindgrenzen en betaald-tijdbeleid;
4. bepaalt het volgende betaalde segment;
5. leest voor `StartNewAction` de huidige action opnieuw bij de provider;
6. vereist dat deze nog `active` is en dezelfde eindtijd heeft;
7. gebruikt `work.Id` als stabiel operation-id voor de continuation.

Tot dit punt past de flow bij de bedoeling van een JIT precheck.

## Bevinding SCHED-001 — T-5 en StartNewAction spreken elkaar tegen

**Status:** bevestigd  
**Prioriteit voor vervolgonderzoek:** hoog

### Codegedrag

Na de T-5 precheck roept de processor voor Oss `ProviderContinuationStartStore.PrepareAttemptAsync(...)` aan.

Deze store weigert echter een nieuwe continuation zolang de voorgaande provideraction nog niet is afgelopen:

```text
if previous.PlannedEndAt > UtcNow
    -> InvalidOperationException
```

Dezelfde store plant de opvolgaction als:

```text
PlannedStartAt = previous.PlannedEndAt + 1 seconde
```

Gevolg:

```text
T-5: scheduler claimt continuation
     -> actuele provideraction is nog geldig/active
     -> PrepareAttemptAsync weigert omdat PlannedEndAt nog in toekomst ligt
     -> exception bereikt VisitSchedulerWorker
     -> ReleaseFailedAsync zet hetzelfde work ongeveer +1 minuut terug naar Pending

T-4: hetzelfde
T-3: hetzelfde
T-2: hetzelfde
T-1: hetzelfde
rond T: pas wanneer PlannedEndAt <= UtcNow kan PrepareAttemptAsync doorgaan
```

### Functionele betekenis

Daarmee is T-5 momenteel **geen moment waarop de opvolgende provideraction alvast veilig wordt gepland**. Het is feitelijk het begin van een exception-gedreven pollingcyclus richting de actiongrens.

Dat wijkt af van de functionele/technische bedoeling die tot nu toe aan T-5 is gekoppeld: vroeg providerstate valideren en de opvolgende dekking JIT voorbereiden.

### Betrouwbaarheidsimpact

De continuation wordt rond de providergrens afhankelijk van:

- het exacte tijdstip van de laatste retry;
- de worker-loop;
- database-claimtijd;
- provider-latency;
- eventuele tijdelijke storing precies rond `PlannedEndAt`.

De nieuwe lokale action gebruikt wel `previous.PlannedEndAt + 1 seconde` als gewenste provider-starttijd, maar de externe start-call wordt pas ná het verstrijken van de vorige actiongrens uitgevoerd.

We moeten bij de vervolgaudit daarom expliciet bewijzen hoe 2Park omgaat met een requested starttijd die op het moment van de call al enkele seconden in het verleden kan liggen. Zonder dat bewijs kan niet worden aangenomen dat betaalde providerdekking gegarandeerd naadloos is.

### Positieve beschermingen die wel aanwezig zijn

Deze bevinding betekent niet dat de hele continuationflow onveilig is. De volgende beschermingen zijn aanwezig:

- schedulerwork wordt persistent opgeslagen;
- claiming gebruikt `FOR UPDATE SKIP LOCKED`;
- dezelfde Visit advisory lock wordt gebruikt bij relevante mutaties;
- `work.Id` is de stabiele operation-id voor continuation;
- `ProviderOperation` voorkomt dat een retry stilzwijgend als een volledig nieuwe onafhankelijke mutatie wordt behandeld;
- vóór StartNewAction wordt de voorgaande provideraction opnieuw remote gecontroleerd;
- unknown providerresultaten gaan naar reconciliation;
- een bevestigde continuation plant indien nodig opnieuw schedulerwork voor de volgende grens.

### Bestaande testdekking

Gevonden dekking bewijst onder andere:

- continuation-work wordt na eerste start persistent aangemaakt;
- de `DueAt` ligt op T-5;
- scheduler claiming en stop-races hebben PostgreSQL-integratiedekking;
- continuation kan een volgende provideraction aanmaken;
- continuation operations worden als `ContinueStart` en `Succeeded` opgeslagen;
- recovery/unknown-result flows hebben afzonderlijke dekking.

### Ontbrekend baseline-bewijs

Tijdens deze audit is nog geen geautomatiseerde end-to-end/integratietest gevonden die de volledige tijdsflow bewijst:

```text
T-5 work claimen
-> StartNewAction continuation verwerken terwijl predecessor nog actief is
-> geen exception/retry polling
-> successor vooraf/scheduled bij provider
-> grens passeren
-> successor actief
-> ononderbroken betaalde dekking
```

De huidige code kan deze flow in deze vorm bovendien niet uitvoeren, omdat `PrepareAttemptAsync` vóór `previous.PlannedEndAt` expliciet weigert.

## Voorlopige conclusie scenario 1

De baseline heeft goede persistente bouwstenen voor locking, operation-idempotency en recovery, maar de feitelijke timing van `StartNewAction` verdient **nog geen betrouwbaarheidsvink**.

De kernvraag voor de volgende stap is niet direct “hoe repareren we dit?”, maar eerst:

> Wat is de gewenste en door 2Park ondersteunde semantiek voor een successor: moet deze op T-5 als scheduled action worden aangemaakt, of pas exact na het eindmoment van de predecessor?

Dat moet worden gekoppeld aan de al uitgevoerde echte 2Park-tests (`startInMinutes=5` werkte en overlap werd door 2Park geweigerd). Daarna kunnen we bepalen welke codeflow en testmatrix de juiste is.

## Auditbacklog

| ID | Scenario | Status |
| --- | --- | --- |
| SCHED-001 | T-5 -> aansluitende `StartNewAction` | ⚠ bevestigd timingvraagstuk |
| SCHED-002 | gratis periode / overnight -> hervatten betaald parkeren | nog te auditen |
| SCHED-003 | handmatig stoppen versus continuation | nog te auditen |
| SCHED-004 | `DesiredEndAt` verkorten | nog te auditen |
| SCHED-005 | `DesiredEndAt` verlengen | nog te auditen |
| SCHED-006 | open-ended rolling horizon | nog te auditen |
| SCHED-007 | `MaxPaidParkingDuration` grens | nog te auditen |
| SCHED-008 | `MaxVisitElapsedDuration` grens | nog te auditen |
| SCHED-009 | provider timeout/unknown continuation | nog te auditen |
| SCHED-010 | crash/restart tijdens claimed work | nog te auditen |
| SCHED-011 | externe providerwijziging / discrepancy | nog te auditen |
| SCHED-012 | Long Visit warning schedulergedrag | nog te auditen |
