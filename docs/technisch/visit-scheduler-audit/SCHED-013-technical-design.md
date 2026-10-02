# SCHED-013 — technisch ontwerp terminale Visit-finalization

**Status:** ontwerp gereed voor implementatie  
**Datum:** 2 oktober 2026  
**Scope:** natuurlijke Visit-afronding, persistente eindreden, terminal scheduler-work en recovery

Dit document werkt de functionele besluiten uit [SCHED-013](SCHED-013.md) technisch uit. Er wordt in deze stap nog geen productielogica gewijzigd.

## 1. Hoofdkeuze: `StopVisit` blijft de uniforme terminale orchestration

Er komt geen nieuw `EndVisit` scheduler-worktype.

`VisitSchedulerWorkType.StopVisit` blijft verantwoordelijk voor iedere tijdgestuurde terminale overgang van een Visit. Daarmee gebruiken handmatig stoppen, deferred stop na end-time shortening en natuurlijke Visit-afronding dezelfde bestaande bouwstenen:

- Visit advisory lock;
- `VisitStatus.Active -> Stopping`;
- durable root `ProviderOperation` van type `Stop`;
- afhandeling van alle niet-terminale `ProviderParkingAction`s;
- provider stop/cancel + reconciliation waar nodig;
- `StopVisitFinalizer`;
- `VisitStatus.Stopping -> Completed`.

De reden waarom de Visit eindigt wordt dus niet gemodelleerd als een nieuw worktype, maar als expliciete domeinstate.

### Waarom geen `EndVisit`

Een apart `EndVisit`-worktype zou uiteindelijk alsnog dezelfde provider- en finalizationflow moeten uitvoeren. Dat creëert twee orchestrationpaden voor dezelfde lifecycle-invariant en vergroot de kans op afwijkend retry-, recovery- en lockinggedrag.

`StopVisit` betekent technisch: **maak deze Visit terminal en laat geen providerdekking achter**. De trigger kan handmatig of automatisch zijn.

## 2. Persistente `VisitEndReason`

Voeg een domeinenum toe:

```text
VisitEndReason
- ManualStop
- DesiredEndReached
- MaxVisitElapsedDurationReached
- MaxPaidParkingDurationReached
```

De `Visit` krijgt:

```text
VisitEndReason? EndReason
```

### Lifecycleregels

- `EndReason == null` zolang een Visit `Starting` of `Active` is.
- `BeginStopping(reason)` zet de reden atomair samen met `Status = Stopping`.
- `EndReason` verandert daarna niet meer.
- `Complete(actualEndAt)` behoudt de eerder vastgelegde `EndReason`.
- `Cancelled` Visits krijgen geen `VisitEndReason`; cancellation tijdens de startflow is geen afgeronde parkeervisit.

Hierdoor blijft de eindreden beschikbaar wanneer:

- de applicatie crasht in `Stopping`;
- provider Stop in `Unknown` staat;
- startup recovery de flow hervat;
- de Visit later in beheer/historie wordt onderzocht;
- SCHED-018 de scheduler-timeline gaat vullen.

De root `ProviderOperation` hoeft daarom geen functionele eindreden te dragen; die beschrijft providerwerk en niet waarom de Visit functioneel eindigt.

## 3. Eindreden op scheduler-work

Een automatisch gepland `StopVisit` workitem moet zijn bedoelde eindreden persistent kennen voordat het due wordt.

Voorgestelde uitbreiding:

```text
VisitSchedulerWork.EndReason : VisitEndReason?
```

Regels:

- verplicht voor automatisch/natuurlijk `StopVisit` work;
- optioneel voor bestaande deferred Stop-work dat tijdens de overgang naar dit ontwerp nog geen reden heeft;
- `null` voor `ContinueProviderCoverage` en `LongVisitWarning`;
- bij uitvoering wordt de reden doorgegeven aan `BeginStopping(reason)`;
- bij recovery kan terminal work exact opnieuw worden opgebouwd uit de berekende terminale grens en reden.

Handmatige Stop maakt geen scheduler-work nodig; daar wordt `ManualStop` rechtstreeks aan de Stop-claim doorgegeven.

## 4. Eén bron voor de effectieve terminale grens

Introduceer één gedeelde calculator/service, conceptueel:

```text
VisitTerminalBoundaryCalculator
```

Output:

```text
VisitTerminalBoundary
- At : DateTimeOffset
- Reason : VisitEndReason
```

of `null` wanneer de Visit geen berekenbare terminale grens heeft.

Deze calculator is de enige bron voor:

- Visit start;
- `DesiredEndAt` wijzigen;
- scheduler planning;
- startup recovery;
- verificatie vlak vóór terminal work wordt uitgevoerd.

### Kandidaten

De calculator bepaalt de vroegste grens uit:

1. `DesiredEndAt` → `DesiredEndReached`;
2. `StartAt + MaxVisitElapsedDuration` → `MaxVisitElapsedDurationReached`;
3. betaald-tijdboundary voor `MaxPaidParkingDuration` → `MaxPaidParkingDurationReached`.

### Gelijke grens

Wanneer meerdere oorzaken exact op hetzelfde tijdstip vallen, gebruiken we voor V1 deze vaste prioriteit:

1. `MaxPaidParkingDurationReached`;
2. `MaxVisitElapsedDurationReached`;
3. `DesiredEndReached`.

Reden: harde policygrenzen zijn afdwingende systeemlimieten; een gelijktijdige gewenste eindtijd maakt de Visit niet minder policy-begrensd. De tijdgrens zelf blijft uiteraard identiek.

Deze tie-break moet in één unit test expliciet worden vastgelegd zodat verschillende codepaden nooit een andere reden kiezen.

## 5. Berekenen van `MaxPaidParkingDuration`

De wall-clock boundary voor maximale betaalde tijd wordt niet opnieuw op meerdere plekken geïmplementeerd.

De terminal-boundary calculator hergebruikt dezelfde versioned `ParkingRuleSet`-segmentatie en paid-time-calculatie die de coverageplanning al gebruikt.

Conceptueel:

```text
paidLimitBoundary =
    first instant >= Visit.StartAt
    where accumulated paid duration == MaxPaidParkingDuration
```

Gratis intervallen verbruiken de limiet niet.

Voorbeeld:

```text
Visit start              18:00
paid window eindigt      20:00
volgende paid start      09:00
MaxPaidParkingDuration   4h

=> 2h betaald op dag 1
=> nog 2h betaald op dag 2
=> terminal boundary 11:00 dag 2
```

Daarmee is de Visit tijdens de gratis nacht nog functioneel `Active`, maar mag hij na 11:00 geen providerdekking of actieve lifecycle meer hebben.

## 6. Terminal work plannen

Introduceer één idempotente planner, conceptueel:

```text
EnsureTerminalWorkAsync(Visit visit)
```

Gedrag onder de Visit-lock:

1. bereken de actuele `VisitTerminalBoundary`;
2. zoek bestaande `Pending`/`Claimed` terminale `StopVisit` work;
3. geen grens:
   - cancel obsolete pending terminal work;
   - geen nieuw terminal work;
4. grens bestaat en exact passend work bestaat:
   - niets doen;
5. grens/reason gewijzigd:
   - cancel obsolete pending work;
   - maak exact één nieuw `StopVisit` work op `boundary.At` met `boundary.Reason`;
6. claimed work dat gelijktijdig met een end-time wijziging concurreert wordt niet blind gemuteerd; dit wordt samen met SCHED-014 via de uniforme Visit-lock/lock-order opgelost.

Invariant voor iedere `Active` Visit met berekenbare grens:

```text
exact één relevante Pending/Claimed terminale StopVisit-taak
```

## 7. Waar de planner wordt aangeroepen

### Na succesvolle Visit-start

Zodra de Visit `Active` is geworden:

- terminal boundary berekenen;
- terminal `StopVisit` work verzekeren;
- coverage-work onafhankelijk daarvan plannen.

Ook een volledig gratis Visit krijgt dus terminal work.

### Na `DesiredEndAt` wijziging

Na het persistent wijzigen van `DesiredEndAt`, binnen dezelfde Visit-gecoördineerde flow:

- boundary opnieuw berekenen;
- terminal work vervangen indien nodig;
- coverage-work daarna aanpassen aan dezelfde boundary.

### Startup recovery

Voor iedere `Active` Visit:

- boundary opnieuw berekenen;
- ontbrekend/verouderd terminal work herstellen;
- wanneer `boundary.At <= now`: terminal work direct due maken;
- daarna pas continuation reconstrueren binnen de nog geldige terminale horizon.

## 8. Uitvoering van terminal `StopVisit` work

Wanneer een terminal workitem due is:

1. verkrijg Visit advisory lock volgens de uniforme lock-order uit SCHED-014;
2. herlees Visit;
3. als `Completed`/`Cancelled`: work idempotent afronden/cancelen;
4. als al `Stopping`: bestaande root Stop-operation/recovery volgen;
5. als `Active`:
   - terminal boundary opnieuw berekenen;
   - als de opgeslagen boundary/reason niet meer actueel is: obsolete work annuleren en actuele planning herstellen;
   - als boundary nog in de toekomst ligt: work releasen/vervangen;
   - anders `BeginStopping(work.EndReason)`;
6. alle andere continuation/warning work voor deze Visit niet meer laten voortzetten;
7. bestaande Stop/provider/finalizationflow uitvoeren.

Belangrijk: de actuele Visit-state wordt op execution-time altijd opnieuw gevalideerd. Scheduler-work is een durable intent, geen toestemming om blind een oude planning uit te voeren.

## 9. `ActualEndAt`

De Stop/finalizationflow moet twee soorten eindtijd ondersteunen.

### Handmatige Stop

```text
ActualEndAt = daadwerkelijk functioneel stopmoment
```

Dit blijft het actuele `TimeProvider.GetUtcNow()`-moment van de Stop-flow.

### Automatische terminale grens

```text
ActualEndAt = VisitTerminalBoundary.At
```

Ook wanneer de worker enkele seconden/minuten later draait na vertraging of restart.

Voorbeeld:

```text
terminal boundary  = 20:00:00
worker restart     = 20:02:13
Visit CompletedAt  = verwerking rond 20:02:13
Visit ActualEndAt  = 20:00:00
```

Provider timestamps veranderen deze functionele Visit-eindtijd niet.

## 10. Provideractions op de terminale grens

Finalization blijft verboden zolang een provideraction mogelijk nog werk vereist.

Per state:

- `Active`: stoppen wanneer hij functioneel voorbij/door de Visitgrens loopt;
- `Scheduled`: annuleren wanneer hij nog zou starten of doorlopen na de Visitgrens;
- `Starting`/unknown mutation: eerst reconciliation;
- `Completed`/`Stopped`/`Failed`: geen provideractie meer nodig.

Een provideraction die vanzelf exact op de Visitgrens expireert mag als `Completed` worden bevestigd en hoeft niet kunstmatig met een Stop-call te worden beëindigd.

De technische provider-state/timestampdetails worden verder uitgewerkt in SCHED-001/SCHED-002/SCHED-017; SCHED-013 vereist alleen de invariant dat niets niet-terminaals de Visit mag overleven.

## 11. Race: terminal work versus continuation

Op of na `VisitTerminalBoundary.At` mag continuation nooit nieuwe providerdekking creëren.

Daarom moet zowel:

- de scheduler-claim/state-policy uit SCHED-015; als
- `ProviderContinuationStartMutationGuard`

de terminale boundary opnieuw controleren.

Zelfs wanneer continuation eerder geclaimd is, moet de provider-mutationguard vlak vóór de externe mutatie blokkeren zodra de terminale grens bereikt of gepasseerd is.

Terminal finalization wint semantisch altijd.

## 12. Race: handmatige Stop versus terminal work

Beide paden komen onder dezelfde Visit advisory lock samen.

### Manual Stop wint eerst

- Visit krijgt `EndReason = ManualStop`;
- status wordt `Stopping`;
- pending terminal work wordt geannuleerd;
- later terminal work is no-op/idempotent.

### Terminal work wint eerst

- Visit krijgt automatische `EndReason`;
- status wordt `Stopping`;
- latere handmatige Stop sluit aan op de reeds bestaande Stop-operation;
- de oorspronkelijke automatische eindreden blijft immutable.

Hierdoor is de uitkomst deterministisch en recovery-safe.

## 13. Relatie met SCHED-015

`StopVisit` is safety/terminal work en mag niet generiek worden geannuleerd omdat `Visit.Health != Healthy`.

Voor terminal Stop geldt:

- `Active + Healthy` → uitvoeren;
- `Active + Reconciling/AttentionRequired` → nog steeds terminal orchestration uitvoeren, maar unresolved providerstate via reconciliation afhandelen;
- `Stopping` → bestaande Stop/recovery vervolgen;
- `Completed/Cancelled` → work idempotent sluiten.

Dit is een belangrijk inputpunt voor het latere work-type state-policy ontwerp in SCHED-015.

## 14. Relatie met SCHED-014

De implementatie van `EnsureTerminalWorkAsync`, scheduler claim en Stop-claim mag pas worden afgerond nadat één lock-order is gekozen.

SCHED-013 vereist alleen:

- planning en lifecycle-transition onder dezelfde Visit-coördinatie;
- geen pad dat eerst scheduler-row lockt en daarna tegengesteld wacht op Visit-lock wanneer een ander pad Visit-lock → scheduler-row gebruikt.

De concrete lock-order wordt in SCHED-014 definitief ontworpen.

## 15. Recovery-invariant

Na startup recovery geldt per Visit:

### `Active`

- terminal boundary herberekend;
- exact één actuele terminal task indien boundary bestaat;
- continuation alleen vóór boundary;
- overdue terminal task direct uitvoerbaar.

### `Stopping`

- geen nieuwe terminal task creëren;
- bestaande Stop-operation herstellen/reconciliëren;
- `EndReason` moet al op Visit staan.

### `Completed` / `Cancelled`

- geen pending/claimed terminal of continuation work laten bestaan.

## 16. Migratie-impact vóór V1

De implementatie vereist naar verwachting nieuwe persistente velden:

```text
Visit.EndReason nullable
VisitSchedulerWork.EndReason nullable
```

Omdat de database vóór V1 nog mag worden gereset en migraties aan het einde worden geconsolideerd, hoeft deze ontwerpstap geen compatibiliteitslaag voor historische pre-V1 data te introduceren.

Bij uiteindelijke migratie moeten bestaande completed Visits zonder EndReason nullable blijven of via expliciete backfillsemantiek worden behandeld; geen eindreden gokken uit timestamps.

## 17. Implementatievolgorde voor SCHED-013

SCHED-013 kan in kleine commits worden gebouwd:

1. domeinmodel `VisitEndReason` + Visit lifecycle-tests;
2. centrale `VisitTerminalBoundaryCalculator` + paid/max/tie tests;
3. terminal work metadata + idempotente `EnsureTerminalWorkAsync`;
4. Visit start + end-time change planning;
5. startup recovery rebuild;
6. `StopVisit` execution met automatische reason/`ActualEndAt`;
7. terminal-vs-continuation guard;
8. integratietests voor paid/free/overnight/policy/restart/manual-stop races.

De onderdelen die direct afhangen van de nog open SCHED-014 lock-order of SCHED-015 claim-policy worden niet lokaal om die bevindingen heen gepatcht; daarvoor wordt eerst het gedeelde ontwerp gevolgd.

## 18. Definition of done

Het technische ontwerp is correct geïmplementeerd wanneer:

- iedere berekenbaar eindige Active Visit durable terminal work heeft;
- `Visit.EndReason` betrouwbaar door Stop/recovery/finalization behouden blijft;
- één calculator overal dezelfde boundary en reason oplevert;
- automatisch einde dezelfde Stop/provider/finalizationflow gebruikt als handmatig stoppen;
- gratis Visits zonder provideraction automatisch eindigen;
- `ActualEndAt` bij automatisch einde de functionele boundary is;
- restart na de boundary alsnog correct completeert;
- manual Stop en terminal work deterministisch/idempotent concurreren;
- geen providercoverage na de terminale grens kan worden gestart;
- afgeronde Visits geen actieve scheduler/providerstate achterlaten;
- de verificatiecriteria uit SCHED-013 groen zijn.
