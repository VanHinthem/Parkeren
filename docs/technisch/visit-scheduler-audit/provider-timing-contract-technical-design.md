# Provider timing contract — technisch ontwerp

**Scope:** SCHED-001, SCHED-002 en SCHED-017  
**Status:** ontwerp voor implementatie  
**Ontwerpdatum:** 2 oktober 2026

## Doel

Dit document legt één technisch contract vast voor:

- just-in-time continuation van providerdekking;
- future/scheduled provideractions;
- hervatten van providerdekking na gratis perioden;
- overgang `Scheduled -> Active -> Completed`;
- reconciliation na timeout/restart;
- tolerante matching van provider timestamps.

De drie auditbevindingen worden bewust samen ontworpen omdat ze dezelfde state machine en hetzelfde providercontract delen.

## Bevestigd providercontract uit live 2Park-tests

Voor Oss is live bevestigd:

- een action met `TIMESTART` vijf minuten in de toekomst krijgt direct een provider action-id;
- zo'n action heeft read-back status `scheduled`;
- een scheduled action kan voor de start worden geannuleerd;
- `B.Start == A.End` wordt geweigerd als overlap (`PRK-00005`);
- `B.Start = A.End + 1 seconde` wordt geaccepteerd terwijl A nog `active` is en B `scheduled` wordt;
- provider timestamps kunnen enkele seconden afwijken van lokaal aangevraagde waarden;
- locatie-readback kan een gebruikerslabel teruggeven terwijl mutation een locatiecode gebruikt;
- voor Oss is `Continuation = StartNewAction` de V1-strategie.

Daarom mag de implementatie `scheduled` nooit als onzekere of mislukte start behandelen wanneer de action-id en overige identiteit kloppen.

## Kerninvarianten

### 1. Maximaal één toekomstige successor

Per Visit mag maximaal één niet-geactiveerde scheduled successor bestaan.

```text
active predecessor
+ maximaal één scheduled successor
```

Er wordt geen keten van meerdere toekomstige actions vooruit gepland.

### 2. JIT vanaf T-5

Voor een betaalde aansluitende successor wordt continuation-work uitvoerbaar vanaf:

```text
predecessor.PlannedEndAt - 5 minuten
```

Vanaf dat moment mag de provider mutation direct plaatsvinden.

De lokale successor krijgt:

```text
PlannedStartAt = predecessor.PlannedEndAt + 1 seconde
```

De provider mutation hoeft dus niet te wachten tot de predecessor lokaal of remote beëindigd is.

### 3. Scheduled is een bevestigde mutation

Wanneer provider read-back na een future start dezelfde provider action-id teruggeeft met status `scheduled`, geldt de mutation als bevestigd.

De operation gaat dan naar `Succeeded`; de lokale `ProviderParkingAction` blijft `Scheduled`.

`scheduled` is geen reconciliation-state.

### 4. Lokale planning is functioneel leidend

Businesslogica gebruikt lokale geplande grenzen:

- `PlannedStartAt`;
- `PlannedEndAt`;
- Visit terminal boundary;
- ParkingRuleSet-segmenten.

Provider timestamps zijn observaties en bewijs voor reconciliation, maar mogen de Visit- of policygrenzen niet achteraf verschuiven.

### 5. Provider action-id is primaire identiteit

Zodra een provider action-id bekend is, is die de primaire sleutel voor read-back matching.

Kenteken, product en timestamps zijn secundaire verificatievelden; ze vervangen de action-id niet.

### 6. Geen exacte timestampgelijkheid

Exacte gelijkheid of 1 ms-tolerantie is niet toegestaan als algemeen providercontract.

Er komt één centrale provider timestamp-tolerantie voor identity/reconciliation. De definitieve waarde moet vóór implementatie nog één keer expliciet worden gekozen en gedocumenteerd; live tests bewijzen alleen dat afwijkingen van enkele seconden voorkomen.

Tot die waarde besloten is, mag geen nieuwe lokale vergelijking hardcoded worden.

## State machine ProviderParkingAction

Voor continuation wordt onderstaande functionele state machine gebruikt:

```text
Starting
   |
   | provider start bevestigd
   v
Scheduled ----------------------+
   |                             |
   | provider meldt active       | Stop Visit / shortening
   v                             |
Active                           |
   |                             |
   | natuurlijke providergrens   |
   v                             |
Completed                        |
                                 |
Scheduled/Active ----------------+
   |
   | expliciete stop/cancel bevestigd
   v
Stopped
```

Een onzekere mutation is geen aparte `ProviderParkingAction` businessstate maar wordt gedragen door de bestaande `ProviderOperation`-status (`Unknown`/reconciliation).

## Aansluitende betaalde continuation — nominale flow

Voorbeeld:

```text
A: 12:00:00 -> 16:00:00
B: 16:00:01 -> ...
```

Flow:

```text
15:55:00 continuation work due
-> Visit + terminal boundary valideren
-> controleren dat geen scheduled successor bestaat
-> B persistent voorbereiden
-> provider Start(B)
-> provider geeft action-id terug
-> read-back B
-> status scheduled accepteren
-> ProviderOperation Succeeded
-> B lokaal Scheduled
-> continuation work afronden
```

Er hoeft na deze bevestiging geen continuation-work tot 16:00 actief te blijven uitsluitend om B te 'bewaken'. De overgang naar Active hoort bij periodieke/scheduled provider-state reconciliation.

## Scheduled -> Active overgang

Een lokale scheduled action moet rond `PlannedStartAt` opnieuw tegen de provider worden gelezen.

Gewenste semantiek:

```text
remote scheduled vóór start
-> lokaal Scheduled houden

remote active rond/na start
-> lokaal ActivateScheduled(...)
-> predecessor indien verstreken lokaal Completed
-> continuation voor nieuwe active action plannen
```

Als de provider vlak na `PlannedStartAt` nog `scheduled` teruggeeft, is dat niet meteen een discrepancy. Er moet een korte expliciete activation grace/tolerance bestaan.

De exacte grace-periode blijft een implementatieparameter die vóór codewijziging nog wordt gekozen op basis van worker-cadans en providerwaarneming.

Na overschrijding van die grace zonder geldige overgang volgt reconciliation/AttentionRequired in plaats van blind nieuwe providerdekking.

## Gratis periode / overnight

De huidige flow wacht bij een gratis gat tot exact het volgende betaalde begin voordat de providerstart wordt uitgevoerd. Dat is te laat voor betrouwbare dekking.

Nieuwe invariant:

> Ook de eerste action na een gratis interval wordt JIT als toekomstige scheduled action aangemaakt.

Voor een volgend betaald segment met start `PaidStart`:

```text
provider mutation due = PaidStart - 5 minuten
ProviderParkingAction.PlannedStartAt = PaidStart
```

De `+1 seconde`-regel geldt alleen wanneer de nieuwe action direct aansluit op een vorige provideraction en 2Park-overlap moet worden vermeden.

Na een echt gratis gat is er geen overlap met de vorige action; dan begint de nieuwe action exact op het begin van het betaalde segment.

Voorbeeld overnight:

```text
zaterdag paid tot 20:00
zondag gratis
maandag paid vanaf 09:00
```

Dan wordt maandag-action rond 08:55 als `scheduled` bij 2Park aangemaakt met `PlannedStartAt = maandag 09:00`.

De Visit blijft tijdens de gratis periode gewoon `Active`; er is alleen geen providerdekking nodig.

## Predecessor na natuurlijke eindtijd

Voor continuation na een gratis gat mag de implementatie niet eisen dat de vorige provideraction remote nog `active` is.

Na `PlannedEndAt` kan een provideraction immers natuurlijk afgelopen zijn.

De correcte vraag is dan:

1. bestaat de bekende predecessor nog of is zijn natuurlijke afloop aantoonbaar;
2. is er geen aanwijzing voor een onverwachte externe wijziging die de Visit onveilig maakt;
3. is de nieuwe dekking volgens lokale regels nog nodig.

Exact welk post-end status/visibility-model 2Park gebruikt is nog niet live bewezen. Tot dat contract bekend is, moet de mock/testharness hier geen verzonnen providerstatus afdwingen.

## Timestampmatching

### Primaire match

Wanneer `ProviderActionId` bekend is:

```text
ProviderActionId exact
+ productcontext passend
+ kenteken passend waar beschikbaar
+ status semantisch toegestaan
```

Timestampverschillen binnen de afgesproken tolerantie zijn dan geen reden om de mutation af te wijzen.

### Fallback na unknown zonder bekende action-id

Wanneer een provider mutation mogelijk is uitgevoerd maar geen action-id bekend is, mag fallback-matching alleen binnen een beperkte zoekruimte plaatsvinden:

- dezelfde Visit/providerproduct-context;
- hetzelfde genormaliseerde kenteken;
- starttijd binnen provider-tolerantie van `PlannedStartAt`;
- eindtijd binnen provider-tolerantie van `PlannedEndAt`;
- semantisch passende status (`scheduled` of `active` afhankelijk van tijdstip);
- resultaat moet uniek zijn.

Geen unieke kandidaat betekent `Unknown`/reconciliation; nooit blind opnieuw starten.

## Location matching

Provider-location read-back is geen betrouwbare exacte stringmatch omdat live is gezien dat start een code gebruikt (`OSS_J`) en read-back een label kan leveren (`OSS Zone J`).

Daarom:

- mutation gebruikt de persistente provider-location/code van de Visit;
- reconciliation gebruikt locatie niet als harde identity equality tenzij de provider later een stabiele machine-id levert;
- een puur labelverschil veroorzaakt geen discrepancy.

## Relatie met terminal Visit boundary

Voor iedere geplande nieuwe provideraction geldt:

```text
PlannedEndAt <= VisitTerminalBoundary
```

Een successor wordt niet aangemaakt wanneer de terminale Visitgrens vóór of op de geplande start ligt.

Als Stop/finalization wint terwijl een scheduled successor al bestaat:

- scheduled action annuleren;
- actieve action stoppen indien nodig;
- pas daarna Visit finaliseren.

Dit sluit aan op SCHED-013.

## Relatie met DesiredEndAt shortening

Bij shortening moet een bestaande scheduled successor opnieuw worden geclassificeerd:

1. volledig na nieuwe terminal boundary -> annuleren;
2. start vóór boundary maar end erna -> annuleren en alleen vervangen wanneer een nieuwe geldige kortere provideraction nodig is;
3. volledig binnen boundary -> mag blijven bestaan.

Deze beoordeling gebeurt durable onder dezelfde Visit-lock als de end-time mutation.

## Reconciliation en restart

Startup recovery moet scheduled actions als geldige bekende providerstate behandelen.

Na restart:

- `Scheduled` vóór start -> read-back bevestigen en behouden;
- `Scheduled` rond/na start -> remote status controleren en zo nodig lokaal activeren;
- `Active` -> reguliere active reconciliation;
- unknown ProviderOperation -> eerst reconciliation, geen nieuwe successor starten;
- ontbrekend continuation-work -> pas herbouwen nadat bestaande scheduled/active actions zijn geïnventariseerd.

Zo wordt een reeds geaccepteerde future mutation nooit door restart gedupliceerd.

## Centrale helpers / verantwoordelijkheden

Voor implementatie wordt de logica geconsolideerd in expliciete componenten in plaats van losse timestampchecks in stores/processors.

### `ProviderActionMatchPolicy`

Verantwoordelijk voor:

- action-id matching;
- genormaliseerd kenteken/product;
- timestamp tolerance;
- toegestane providerstatussen;
- unieke fallback-match bij onbekende action-id.

### `ProviderContinuationSchedule`

Verantwoordelijk voor:

- JIT mutation moment (`T-5`);
- `End + 1 seconde` voor directe aansluiting;
- exact paid-window start na een gratis interval;
- limiteren op terminal Visit boundary;
- maximaal één scheduled successor.

De bestaande `ProviderCoverageSchedule` kan hiervoor worden uitgebreid of opgesplitst; belangrijk is één bron van waarheid.

### Scheduled activation reconciliation

Er moet één pad verantwoordelijk zijn voor:

- `Scheduled -> Active`;
- grace rond activation;
- predecessor `Completed` markeren;
- volgende continuation work plannen.

Dit gedrag mag niet verspreid blijven over generic continuationbranches.

## Nog expliciet open vóór implementatie

### 1. Timestamp tolerance

Live bewijs zegt 'enkele seconden', maar niet de veilige bovengrens.

Voor codewijziging bepalen we één concrete tolerantie en gebruiken die overal via `ProviderActionMatchPolicy`.

### 2. Activation grace

Hoe lang na `PlannedStartAt` mag 2Park nog `scheduled` teruggeven voordat dit als afwijking geldt?

Dit moet passen bij providerlatency en worker-cadans.

### 3. Post-end providerstatus

Nog live vast te stellen:

- blijft een natuurlijk afgelopen action zichtbaar;
- met welke status;
- of verdwijnt hij uit current actions.

Dit is vooral relevant voor SCHED-002/SCHED-011 en de TwoParkMock.

### 4. Provider-capaciteit scheduled actions

Issue #71 heeft nog niet bevestigd of scheduled actions meetellen voor providercapaciteit. De scheduler mag voor V1 maximaal één successor per Visit maken, maar capacity-handling moet na die providerbevinding nog worden gevalideerd.

Deze open punten mogen niet met aannames in de mock worden ingevuld.

## Teststrategie na implementatie

Minimaal geautomatiseerd bewijzen:

1. T-5 continuation maakt direct één future action;
2. `scheduled` read-back resulteert in Succeeded, niet Unknown;
3. tweede scheduled successor wordt niet gemaakt;
4. directe aansluiting gebruikt predecessor end + 1 seconde;
5. gratis->betaald maakt action T-5 vóór `PaidStart` met start exact op `PaidStart`;
6. overnight scenario houdt Visit actief zonder providerdekking en plant volgende paid action vooruit;
7. restart met bestaande scheduled action dupliceert niets;
8. scheduled->active overgang wordt lokaal verwerkt;
9. timestamps binnen tolerance worden geaccepteerd;
10. unieke fallback-match na unknown werkt;
11. meerdere fallback-kandidaten blijven Unknown;
12. locatie label/code verschil veroorzaakt geen foutieve discrepancy;
13. terminal Visit boundary blokkeert successor buiten de Visit;
14. shortening annuleert/vervangt scheduled successor correct;
15. Stop Visit ruimt active en scheduled actions op.

TwoParkMock-tests voor natuurlijke providerexpiry worden pas toegevoegd nadat het echte post-end providercontract bekend is.

## Voorgestelde implementatievolgorde

Na akkoord op dit ontwerp:

1. centrale provider match/timestamp policy introduceren;
2. directe start-readback en reconciler daarop aansluiten;
3. continuation store toestaan vanaf JIT-venster en `scheduled` als succes verwerken;
4. scheduled activation/reconciliation expliciet maken;
5. gratis/overnight planning naar T-5 future start wijzigen;
6. restart/recovery tegen scheduled actions hardenen;
7. shortening/Stop regressies uitvoeren;
8. TwoParkMock alleen aanpassen voor providergedrag dat daadwerkelijk bevestigd is;
9. integrale tests voor SCHED-001/002/017.

## Relatie met andere audititems

- **SCHED-003:** Stop moet scheduled successor veilig annuleren.
- **SCHED-004/005:** end-time wijziging moet future action opnieuw beoordelen.
- **SCHED-009:** timeout/reconciliation gebruikt dezelfde match policy.
- **SCHED-011:** discrepancy-detectie gebruikt dezelfde timestampsemantiek.
- **SCHED-013:** terminal Visit boundary begrenst providerdekking.
- **SCHED-014:** alle mutaties volgen later de uniforme lock-order.
- **SCHED-015:** work-type gating bepaalt of activation/Stop/reconciliation mogen lopen.
- **SCHED-016:** mock volgt pas bevestigd providergedrag.
