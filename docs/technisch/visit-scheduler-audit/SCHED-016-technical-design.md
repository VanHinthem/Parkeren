# SCHED-016 — TwoParkMock en boundary-teststrategie

**Status:** technisch ontwerp  
**Prioriteit:** middel/hoog  
**Ontwerpdatum:** 2 oktober 2026  
**Raakt:** SCHED-001, SCHED-002, SCHED-009, SCHED-011, SCHED-013, recovery en integrale schedulerproof.

> **Historische ontwerpnotitie:** de statische-statusbeschrijving onder “As-built probleem” is achterhaald. De mock heeft inmiddels een centrale bestuurbare klok en dynamische `scheduled -> active`-read-back; zie [SCHED-016](SCHED-016.md) en [PROGRESS](PROGRESS.md) voor de actuele implementatie. Live 2Park-post-End-status en scheduled-capacity blijven wel onbevestigd.

## Doel

TwoParkMock moet schedulergrenzen deterministisch kunnen bewijzen zonder providergedrag te verzinnen dat nog niet live is bevestigd.

De mock blijft primair een test-double voor 2Park, niet een tweede business-engine. Alleen gedrag dat door live 2Park-tests of expliciete productregels is bevestigd wordt automatisch gemodelleerd. Onbewezen providersemantiek blijft configureerbaar of expliciet buiten de automatische state machine.

## As-built probleem

De huidige mock bepaalt alleen bij creatie:

```text
Start > now  -> scheduled
anders       -> active
```

Daarna blijft `Status` statisch opgeslagen. Daardoor:

- een future action wordt niet vanzelf `active`;
- een verstreken active action blijft `active`;
- scheduler/recoverytests rondom tijdsgrenzen kunnen een providerbeeld zien dat niet overeenkomt met echte 2Park-semantiek.

De huidige mock is wel al geschikt voor:

- start/stop mutations;
- future start aanmaken;
- unknown outcome;
- visibility delay;
- validatie-/auth-fouten;
- max action duration;
- capaciteit;
- persistente teststate over mock restart.

Die mogelijkheden blijven behouden.

## Ontwerpprincipe: persist intent, derive observable state

We slaan de provideraction als intent/data op:

- `Id`;
- `LicensePlate`;
- `Start`;
- `End`;
- `Location`;
- `ProductId`;
- expliciete terminale mutation-state, bijvoorbeeld `Stopped` indien stop/cancel is uitgevoerd;
- visibility metadata.

Voor niet-expliciet gestopte actions wordt de **waarneembare providerstatus** bij read-back afgeleid uit de testklok en het bewezen providercontract.

Conceptueel:

```text
if explicitly stopped/cancelled:
    stopped
else if now < Start:
    scheduled
else if Start <= now < End:
    active
else:
    post-end behavior
```

De eerste drie takken zijn voldoende bewezen voor V1-tests. De laatste tak blijft afhankelijk van nog te bevestigen live 2Park-gedrag.

## Deterministische testklok

TwoParkMock krijgt een expliciete, testbare klok in plaats van overal `DateTimeOffset.UtcNow` direct te gebruiken.

Voor normale development/runtime kan die klok standaard de echte UTC-tijd volgen.

Voor integratietests moet tijd deterministisch kunnen worden bestuurd, bijvoorbeeld via test-endpoints:

```text
POST /api/test/clock/set
POST /api/test/clock/advance
POST /api/test/clock/reset
```

De exacte endpointnamen zijn implementatiedetail, maar de volgende eigenschappen zijn vereist:

- één centrale bron voor `now` in de mock;
- geen echte `Task.Delay` van minuten nodig om schedulerboundaries te testen;
- reset zet de klok terug naar realtime/default;
- clock control is alleen voor de mock/testomgeving.

Alle tijdsafhankelijke mockfunctionaliteit gebruikt deze klok, inclusief:

- actionstatus;
- visibility delay;
- capaciteit op basis van actuele providerstate;
- response timestamps waar relevant.

## Bevestigde automatische state transitions

### Future action

Voor een action met `Start > now`:

```text
remote status = scheduled
```

Na testklok-advance tot `Start`:

```text
remote status = active
```

Dit gedrag is live bevestigd doordat future start `scheduled` oplevert en de V1 state machine uitgaat van activatie op de geplande startgrens.

### Expliciete stop/cancel

Wanneer `/stop` succesvol is uitgevoerd:

```text
remote status = stopped
```

De expliciete stopstatus wint altijd van de klok. Een gestopte future action mag dus niet later automatisch `active` worden.

### Post-End

Nog **niet automatisch vastleggen** zolang het echte providercontract niet live is bevestigd.

Open mogelijkheden zijn bijvoorbeeld:

- action blijft zichtbaar met `completed`;
- action blijft zichtbaar met een andere providerstatus;
- action verdwijnt uit current actions;
- provider gebruikt tijdafhankelijke filtering.

Tot dit bewezen is, moeten tests die post-End gedrag nodig hebben dat expliciet configureren of uitsluitend lokale schedulerinvarianten testen zonder een fictieve providerstatus als bewijs te gebruiken.

## Configureerbaar post-End testgedrag

Om recovery/discrepancytests toch gericht te kunnen schrijven, mag de mock een **expliciete testmodus** ondersteunen voor post-End gedrag.

Bijvoorbeeld conceptueel:

```text
PostEndBehavior = KeepActive | ReturnCompleted | Hide
```

Belangrijk:

- geen van deze modi wordt als “echte 2Park-default” gepresenteerd totdat live bewijs bestaat;
- tests moeten expliciet aangeven welk providercontract ze simuleren;
- zodra #71 het echte post-End gedrag bevestigt, wordt één mode de default en worden niet-realistische modes alleen voor fault-injection behouden.

## Visibility delay

De bestaande `visibilityDelay` blijft bruikbaar om eventual consistency te testen.

De delay wordt gekoppeld aan de mockklok:

```text
VisibleAt = mockNow + visibilityDelay
```

Daardoor kan een test deterministisch bewijzen:

1. mutation uitgevoerd;
2. directe read-back ziet action nog niet;
3. klok vooruit;
4. action wordt zichtbaar;
5. reconciliation bevestigt de mutation zonder duplicate retry.

Dit is direct relevant voor SCHED-009.

## Timestampnormalisatie/fault injection

Omdat live 2Park timestamps enkele seconden kan normaliseren, moet de mock optioneel gecontroleerde timestamp-afwijking kunnen simuleren.

Conceptueel:

```text
ReadbackStartOffset
ReadbackEndOffset
```

Gebruik hiervan:

- bewijzen dat `ProviderActionMatchPolicy` tolerantie toepast;
- boundary buiten tolerantie testen;
- geen exacte/1ms comparisons meer ongemerkt laten terugkeren.

Default blijft `0` zodat gewone tests eenvoudig blijven.

Deze offsets wijzigen alleen de provider-readback, niet de opgeslagen lokale intent van de mockaction.

## Locatiecode versus label

De mock mag optioneel een ander read-back label teruggeven dan de mutation-locationcode, zodat het live waargenomen patroon kan worden getest:

```text
start: OSS_J
read-back: OSS Zone J
```

Een labelverschil mag volgens het provider timing contract geen false discrepancy veroorzaken.

## Capaciteit

Capaciteitsberekening moet gebaseerd worden op de **afgeleide actuele providerstate**, niet op een ooit opgeslagen statische status.

Voor zover live bewezen:

- `active` telt mee;
- of `scheduled` meetelt is nog open in #71.

Daarom wordt scheduled-capacity voor nu expliciet configureerbaar in de mock en niet als providerfeit hardcoded.

Na live bevestiging wordt de default daarop aangepast.

## Mock persistency en klok

Persistente actions blijven nuttig voor restarttests.

De testklok zelf hoeft niet noodzakelijk mee te persisteren over containerrestart. Voor deterministische restarttests zijn twee geldige strategieën:

1. test zet na restart opnieuw expliciet dezelfde klok; of
2. testclock-state wordt optioneel persistent gemaakt.

Voor V1 heeft optie 1 de voorkeur: eenvoudiger en minder kans dat development onverwacht in een oude fake tijd opstart.

## Integrale boundary-testmatrix

Na schedulerimplementatie moeten minimaal onderstaande scenario's end-to-end met echte PostgreSQL + app + TwoParkMock bewezen worden.

### A. JIT continuation

1. Visit met active action die om 16:00 eindigt;
2. klok naar 15:54:59 -> geen successor;
3. klok naar 15:55:00 -> continuation due;
4. exact één future action wordt aangemaakt;
5. read-back = `scheduled`;
6. geen tweede successor bij scheduler replay/restart.

### B. Scheduled -> active

1. successor staat `scheduled`;
2. klok vóór start -> blijft scheduled;
3. klok op/na start -> read-back active;
4. lokale action wordt Active;
5. predecessor wordt lokaal Completed wanneer passend;
6. volgende continuation wordt gepland.

### C. Directe aansluiting

Bewijs dat successor:

```text
Start = predecessor.End + 1 seconde
```

gebruikt en dat nooit twee toekomstige successors ontstaan.

### D. Gratis -> betaald / overnight

1. Visit loopt door gratis interval;
2. geen provideraction tijdens gratis tijd;
3. T-5 vóór volgende paid start wordt future action aangemaakt;
4. `PlannedStartAt = PaidStart`;
5. Visit blijft Active gedurende gratis periode;
6. terminal Visit-work blijft onafhankelijk bestaan.

### E. Natural Visit end

Voor finite/free-only/paid-tail Visits:

- terminal `StopVisit` work bestaat;
- op boundary gaat Visit naar Stopping;
- provideractions worden veilig terminal gemaakt;
- Visit wordt Completed met functionele `ActualEndAt`;
- capaciteit komt vrij.

Deze tests mogen post-End providersemantiek alleen claimen wanneer de betreffende mockmode door live bewijs is gerechtvaardigd.

### F. Unknown outcome + delayed visibility

1. provider mutation wordt intern aangemaakt;
2. client krijgt timeout/504;
3. operation wordt Unknown;
4. immediate read-back vindt niets door visibility delay;
5. geen duplicate start;
6. klok vooruit;
7. reconciliation vindt dezelfde action;
8. operation Succeeded.

### G. Timestamp tolerance

- start/end read-back enkele seconden verschoven maar binnen tolerance -> match;
- buiten tolerance zonder action-id -> geen match;
- meerdere fallback-kandidaten -> Unknown blijft staan.

### H. Restart

Op verschillende boundaries procesrestart simuleren:

- vóór T-5;
- nadat successor persisted maar vóór provider mutation;
- na provider mutation maar vóór local confirmation;
- met bestaande scheduled successor;
- rond Scheduled -> Active;
- met claimed terminal work;
- na terminal boundary tijdens downtime.

Na restart geldt steeds:

- geen duplicate provider mutation;
- geen verloren terminal/continuation intent;
- recovery voltooit vóór nieuwe claims.

### I. Stop/shortening race

Met scheduled successor aanwezig:

- handmatige Stop annuleert/stop alle open provideractions;
- shortening vóór successor-start annuleert/vervangt durable;
- terminal boundary wint van continuation;
- geen provideraction overleeft Completed Visit.

### J. Locking concurrency

Met echte PostgreSQL parallel uitvoeren:

- schedulerclaim vs Stop;
- schedulerclaim vs end-time change;
- retry/release vs Stop;
- twee workers die hetzelfde work proberen te claimen.

Bewijs:

- geen deadlock;
- maximaal één claim;
- deterministische state na race.

## Testlagen

Niet iedere test hoeft full-stack te zijn.

### Unit tests

Voor pure logica:

- terminal boundary calculator;
- work execution policy;
- provider match policy;
- continuation schedule;
- state classifiers.

### Database integration tests

Voor:

- advisory lock-order;
- `FOR UPDATE` claiming;
- durable terminal work;
- recovery/rebuild;
- idempotency onder concurrency.

### TwoParkMock integration tests

Voor:

- HTTP/providercontract;
- scheduled/active tijdsovergang;
- visibility delay;
- timestamp offsets;
- unknown outcome;
- restart met persistente provideractions.

### Echte 2Park-validatie

Blijft noodzakelijk voor providersemantiek die niet betrouwbaar uit de mock kan worden afgeleid, waaronder minimaal:

- post-End status/visibility;
- scheduled action en provider-capaciteit;
- eventuele activation latency/grace;
- veilige concrete timestamp-tolerantie.

De mock volgt het live contract; de mock bepaalt het contract niet.

## CI

Deterministische boundarytests mogen geen echte minuten wachten en moeten geschikt blijven voor de normale CI-run.

Daarom:

- fake/testclock gebruiken;
- korte fault-injection delays alleen waar milliseconden voldoende zijn;
- geen externe 2Park-calls in CI;
- echte-provider smoke/spike-tests blijven handmatig/expliciet.

## Implementatievolgorde

Na akkoord op het volledige schedulerontwerp:

1. centrale mockklok introduceren zonder behavior change;
2. testclock control toevoegen;
3. read-back status voor `scheduled -> active` dynamisch afleiden;
4. visibility delay naar mockklok omzetten;
5. timestamp/readback offset fault injection toevoegen;
6. locatie label/code fault injection toevoegen;
7. post-End testmodus toevoegen zonder default provideraanname;
8. integrale boundarytests toevoegen na de bijbehorende schedulerfixes;
9. na live #71-bevinding post-End/scheduled-capacity defaults aanscherpen.

## Nog open

1. echte 2Park post-End status/visibility;
2. telt een `scheduled` action mee voor provider-capaciteit;
3. concrete activation grace;
4. concrete provider timestamp-tolerance.

Deze punten worden niet door de mock ingevuld voordat extern bewijs beschikbaar is.

## Definition of Done SCHED-016

SCHED-016 kan naar ✅ wanneer:

- de mock een deterministische klok gebruikt;
- future actions zonder handmatige statusmutatie van `scheduled` naar `active` kunnen gaan;
- expliciet gestopte actions terminal blijven;
- onbewezen post-End gedrag niet stilzwijgend als feit wordt gemodelleerd;
- timestamp-/visibility-providerafwijkingen deterministisch testbaar zijn;
- de schedulerboundarytests geen wall-clock minuten hoeven te wachten;
- restart-, JIT-, overnight-, terminal-, unknown- en concurrencyflows integraal bewezen zijn;
- later bevestigd live 2Park-gedrag als expliciet providercontract in mock en tests wordt verwerkt.
