# SCHED-001 — T-5 en aansluitende StartNewAction

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Scenario:** betaald parkeren met opvolgende provideraction  
**Datum bevinding:** 2 oktober 2026

## Samenvatting

De huidige scheduler plant continuation-work correct op **T-5** voor een aaneengesloten betaald segment, maar `ProviderContinuationStartStore.PrepareAttemptAsync(...)` weigert vervolgens de opvolgende `StartNewAction` zolang de voorgaande provideraction nog niet is afgelopen.

Daardoor is T-5 in de actuele implementatie geen moment waarop een successor veilig vooraf wordt gepland. Het is feitelijk het begin van een exception/retry-cyclus richting de providergrens.

## Baseline-scenario

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

## Gewenste invarianten

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

### 4. Processor op T-5

`VisitSchedulerWorkProcessor`:

1. leest de actuele/scheduled provideraction;
2. berekent opnieuw `PrecheckAt(latestAction.PlannedEndAt)`;
3. controleert functionele eindgrenzen en betaald-tijdbeleid;
4. bepaalt het volgende betaalde segment;
5. leest voor `StartNewAction` de huidige action opnieuw bij de provider;
6. vereist dat deze nog `active` is en dezelfde eindtijd heeft;
7. gebruikt `work.Id` als stabiel operation-id voor de continuation.

Tot dit punt past de flow bij de bedoeling van een JIT precheck.

## Bewezen inconsistentie

Na de T-5 precheck roept de processor voor Oss `ProviderContinuationStartStore.PrepareAttemptAsync(...)` aan.

Deze store weigert een nieuwe continuation zolang de voorgaande provideraction nog niet is afgelopen:

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

## Betrouwbaarheidsimpact

De continuation wordt rond de providergrens afhankelijk van:

- het exacte tijdstip van de laatste retry;
- de worker-loop;
- database-claimtijd;
- provider-latency;
- eventuele tijdelijke storing precies rond `PlannedEndAt`.

De nieuwe lokale action gebruikt wel `previous.PlannedEndAt + 1 seconde` als gewenste provider-starttijd, maar de externe start-call wordt pas na het verstrijken van de vorige actiongrens uitgevoerd.

Daarom is nog niet bewezen dat betaalde providerdekking naadloos blijft wanneer de provider-call enkele seconden of langer na de geplande successor-start plaatsvindt.

## Positieve beschermingen

Deze bevinding betekent niet dat de hele continuationflow onveilig is. De volgende beschermingen zijn aanwezig:

- schedulerwork wordt persistent opgeslagen;
- claiming gebruikt `FOR UPDATE SKIP LOCKED`;
- relevante mutaties gebruiken dezelfde Visit advisory lock;
- `work.Id` is de stabiele operation-id voor continuation;
- `ProviderOperation` bewaakt provider-mutatie/idempotency;
- vóór StartNewAction wordt de voorgaande provideraction opnieuw remote gecontroleerd;
- unknown providerresultaten gaan naar reconciliation;
- een bevestigde continuation plant indien nodig opnieuw schedulerwork voor de volgende grens.

## Bestaande testdekking

Gevonden dekking bewijst onder andere:

- continuation-work wordt na eerste start persistent aangemaakt;
- de `DueAt` ligt op T-5;
- scheduler claiming en stop-races hebben PostgreSQL-integratiedekking;
- continuation kan een volgende provideraction aanmaken;
- continuation operations worden als `ContinueStart` en `Succeeded` opgeslagen;
- recovery/unknown-result flows hebben afzonderlijke dekking.

## Ontbrekend bewijs

Nog niet aangetoond is de volledige tijdsflow:

```text
T-5 work claimen
-> StartNewAction continuation verwerken terwijl predecessor nog actief is
-> successor veilig vooraf/scheduled bij provider
-> grens passeren
-> successor actief
-> ononderbroken betaalde dekking
```

De huidige code kan deze flow in deze vorm niet uitvoeren, omdat `PrepareAttemptAsync` vóór `previous.PlannedEndAt` expliciet weigert.

## Open beslispunt

Voor een fix moet eerst het echte 2Park-contract worden vastgesteld:

> Kan/moet een opvolgende action op T-5 al als scheduled provideraction worden aangemaakt met een starttijd direct na de predecessor, of moet de Start-call pas na het eindmoment van de predecessor worden uitgevoerd?

Relevante eerdere live bevindingen:

- `startInMinutes=5` werkte;
- overlap werd door 2Park geweigerd;
- een aansluitende scheduled action na de eerste action is eerder succesvol getest.

Deze providersemantiek moet opnieuw precies worden gekoppeld aan de gewenste schedulerflow voordat implementatie wordt gewijzigd.

## Besluit / fix

Nog open.

## Verificatiecriteria na fix

SCHED-001 kan pas op ✅ wanneer minimaal bewezen is:

1. T-5 heeft expliciet en voorspelbaar gedrag zonder exception-gedreven polling;
2. successor-start veroorzaakt geen overlapfout;
3. er ontstaat geen onbedoeld gat in betaalde providerdekking;
4. retry/restart kan geen duplicate successor maken;
5. unknown providerresultaat gaat eerst door reconciliation;
6. een integratietest de volledige continuation-boundary afdekt.
