# Implementatieplan scheduler-restpunten

**Status:** uitvoering gestart (SR-004)\
**Bron:** [Scheduler restpunten](scheduler-restpunten.md)\
**Scope:** SR-001 t/m SR-005. SCHED-018 (scheduler-observability) valt buiten scope.

Dit plan maakt de afgesproken werking uit de restpunten uitvoerbaar in kleine, verifieerbare stappen. De restpunten blijven leidend voor de functionele details en scenario's; dit document bepaalt de volgorde, gates en uitrolvoorwaarden. Houd implementatietaken en actuele voortgang bij in de bestaande V1-planning/GitHub issue #92.

## Uitvoerings- en PR-aanpak

- Werk per zelfstandig restpunt op een eigen branch en open een PR naar `main`: SR-003, SR-004, SR-001 en SR-005, in die volgorde. Begin elke branch vanaf de bijgewerkte `main`; merge pas na review, relevante gerichte tests en groene CI.
- Behandel SR-002 als één samenhangende featurebranch en één PR met interne gates 5a t/m 5d. De contract-, historie-, reconciliatie- en saldoverwerking worden pas gezamenlijk naar `main` gemerged als alle gates groen zijn.
- Een merge naar `main` deployt niet automatisch. Voer de productie-uitrol na de afgesproken acceptatie handmatig en gecontroleerd uit, met exact één actieve API/schedulerinstance en zonder overlappende deployment.
- Houd per PR de scope beperkt tot het betreffende restpunt; documenteer in de PR-beschrijving welke gatecriteria en tests zijn afgedekt. Bij een falende gate wordt de PR eerst hersteld; start geen afhankelijke PR voordat de vorige wijziging gemerged is.

## Uitgangspunten

- Wijzig één restpunt of duidelijk afgebakende stap tegelijk. Ga pas door na de exitcriteria van de vorige stap.
- Behoud Visit-first locking. Houd locks niet vast tijdens externe provider-I/O.
- Herhaal provider-mutaties nooit blind na een timeout of restart; herstel via de bestaande read-back/reconciliation-flow.
- De V1-deployment blijft single-instance. Geen overlappende schedulerworkers of rolling deployment.
- Migratie van bestaande administratie en terugval naar een vorige applicatieversie vallen buiten scope.
- Breid scope niet uit naar SCHED-018. Gebruik bestaande logs, tests en gerichte statuscontroles; ontwerp geen algemene observability-oplossing in dit traject.

## Fase 0 — Voorbereiding en baseline

**Doel:** wijzigingen reproduceerbaar maken en de functionele contracten vastzetten voordat gedrag verandert.

1. Maak voor SR-001 t/m SR-005 afzonderlijke werkitems onder de bestaande schedulerplanning. Koppel elk werkitem aan dit plan en aan de verificatie in `scheduler-restpunten.md`.
2. Leg de huidige relevante tests en testcommando's vast. Voer de bestaande scheduler-, recovery-, provider- en visit-lifecycletests uit als baseline.
3. Breng per stap de benodigde persistencewijzigingen in kaart; bestaande administratie migreren is geen onderdeel van dit traject.
4. Bevestig voor SR-002 vóór implementatie de openstaande technische keuzes: model voor provideractie-intervallen en kosten, status van ontbrekende historie, historie-request/paginering en idempotentiesleutel voor reconciliatie.
5. Bewaar geanonimiseerde live-providerwaarnemingen uitsluitend voor de expliciet aangemaakte testactie/testaccount. Gebruik TwoParkMock voor herhaalbare regressietests.

**Gate 0:** baseline slaagt, de SR-002 openstaande keuzes zijn vastgelegd, en elk werkitem heeft concrete tests en een eigenaar. Bij een falende baseline eerst vaststellen of die al bestond; neem geen ongerelateerde reparaties in dit traject op.

## Fase 1 — SR-003: verlopen providerpogingen herstellen

**Doel:** een bij startup nog geldige providerpoging na het verlopen van de lease alsnog herstellen, zonder een lopende request van het huidige proces over te nemen.

- Implementeer periodieke herbeoordeling van verlopen `InProgress`-pogingen via `Unknown` en de bestaande read-back/reconciliation-flow.
- Behoud de vijfminutenlease en maak de controle op actief eigenaarschap/fencing expliciet.
- Voeg de twee kanten van de grens toe: vóór leaseverloop geen herstel of tweede mutatie; na leaseverloop herstel van een abandoned poging.

**Gate 1:** de tests tonen beide leasegrenzen aan, er wordt geen provider-mutatie blind herhaald en een Visit blijft niet in `Starting` hangen nadat de lease is verstreken.

## Fase 2 — SR-004: mislukte release veilig hervatten

**Doel:** tijdelijke databasefouten bij release blokkeren schedulerwerk niet tot een herstart en veroorzaken geen dubbele actieve verwerking.

- Maak release na een verwerkingsfout opnieuw uitvoerbaar zodra persistence bereikbaar is; behoud de fout zichtbaar voor de aanroeper/logging.
- Beperk stale releasepogingen tot de claim waarvoor ze zijn gestart. Een inmiddels `Completed` of `Cancelled` item mag niet worden teruggezet.
- Voeg tests toe voor tijdelijke releasefout, herstel zonder procesherstart, concurrerende worker en stale release na terminale status.

**Gate 2:** work wordt uiteindelijk vrijgegeven of veilig opnieuw planbaar; twee workers verwerken niet dezelfde actieve claim; terminale items blijven terminaal.

## Fase 3 — SR-001: actuele status en Stop/Extend-race

**Doel:** een verlenging kan niet worden toegestaan op basis van verouderde trackingdata en een gelijktijdige Stop kan geen onveilige provideractie achterlaten.

- Laat de guard onder de Visit-lock de actuele Visit- en provideractie-status lezen, onafhankelijk van reeds gevolgde EF-entiteiten.
- Voeg afzonderlijke integratietests toe voor Stop en voor een health-only wijziging die ná de eerste schedulerload vanuit een tweede scope worden vastgelegd.
- Werk de coördinatie van een reeds lopende Extend met Stop uit volgens het afgesproken contract: blokkeer nieuwe Extend-pogingen, bepaal het werkelijke providerresultaat via response/read-back en stop daarna de actie die werkelijk bestaat.
- Houd de Visit-lock niet vast tijdens provider-I/O. Leg in de tests ook de race vast waarin Stop start nadat de guardcontrole is geslaagd.

**Gate 3:** beide stale-state tests blokkeren `ExtendAction`; de gelijktijdige Stop/Extend-scenario's eindigen zonder actieve of ingeplande provideractie en zonder aannames over een mislukte timeout.

## Fase 4 — SR-005: Scheduled-opvolger bij recovery herkennen

**Doel:** een geldige geplande opvolger over een gratis gat blijft dekking bieden na herstart en wordt niet dubbel aangemaakt.

- Laat recovery een lokale `Scheduled`-actie aan de hand van bekende provider-id en verwachte tijden bij de provider bevestigen.
- Herstel de lokale overgang en planning als de provider inmiddels `active` meldt; behoud de bestaande scheduled actie bij status `scheduled`.
- Breid `ProviderFreeGapRecoveryTests.Recovery_does_not_duplicate_scheduled_successor_after_free_gap` uit met Visit-health en vervolg-/terminal-work.
- Houd ontbrekende of afwijkende providerinformatie conservatief: geen tweede provideractie en waar nodig aandacht/review.

**Gate 4:** tests bewijzen behoud van `Healthy`, geen duplicaat, passend vervolg- en terminalwerk, en veilige afhandeling van ontbrekende/afwijkende status voor zowel `scheduled` als inmiddels `active`.

## Fase 5 — SR-002: actiegebruik, Visit-eindtijd en historie

Dit is een contractwijziging en wordt opgesplitst. Begin deze fase pas na Gate 2, zodat duurzame claims/releases beschikbaar zijn voor het nieuwe schedulerwerk.

### 5a. Contract en datamodel

- Werk functionele en technische documentatie bij: Visit-finalisatie, beleidsgrens, `DueAt` en provideractie-intervallen zijn afzonderlijke begrippen.
- Leg vast welke bron de initiële start- en eindtijd levert voor directe/geplande Start, succesvolle app-Stop, recovery na onzekere Stop en provider-geplande Stop.
- Ontwerp opslag voor initiële tijden, historiecorrecties, providerkosten en onvolledige administratie. Een aparte voorlopigheidsmarkering op tijden is niet gewenst.
- Bevestig de historie-requestparameters en paginering aan de hand van live waarneming voordat de adapter die contracten hardcodeert.

**Gate 5a:** domeincontract, benodigde schemawijzigingen en fallback zijn gereviewd; tests beschrijven hoe ontbrekende en vertraagde historie wordt verwerkt.

### 5b. Historiecontract in TwoParkMock en parsercontracttests

Implementeer dit vóór de reconciliatie, zodat de historieflow deterministisch getest kan worden zonder het echte providerformaat gelijk te stellen aan het mock-formaat.

- Breid `Parkeren.TwoParkMock` en de mock-adapter uit met een historie-interface die provideracties, kosten en paginering kan teruggeven. Ondersteun configureerbare vertraagde zichtbaarheid en ontbrekende records.
- Houd het mock-contract gericht op de applicatie-interface. Laat de mock niet het echte 2Park-responseformaat nabootsen als vervanging voor parservalidatie.
- Voeg mocktests toe voor vertraagde zichtbaarheid en retry, paginering waarbij het gezochte record op een latere pagina staat, en een record dat na alle beschikbare pagina's ontbreekt.
- Test de echte 2Park-historieparser afzonderlijk met geanonimiseerde responsefixtures uit `get_action_historie.json`, inclusief `data.actions`, afgeronde status, `TIMESTART`, `TIMEEND`, `COST`, `CURRENCY_DESC` en paginering. Voeg ook fixtures toe voor een ontbrekend record en relevante lege/ontbrekende responsevelden.
- Parserfixtures bewijzen alleen de provider-JSON-interpretatie; mocktests bewijzen de applicatie-interface en het scheduler-/reconciliatiegedrag. Gebruik geen mock-response als bewijs voor de echte parser.

**Gate 5b:** de mock-interface levert historiegegevens met kosten en paginering en ondersteunt vertraagde zichtbaarheid en ontbrekende records; geanonimiseerde echte 2Park-responses en relevante lege/ontbrekende velden worden door de productieparser correct gelezen.

### 5c. Providerhistorie en duurzame reconciliatie

- Sla de eerste provider-eindtijd uit de Start-readback op naast de reeds opgeslagen provider-id en starttijd.
- Plan reconciliatie duurzaam na een succesvolle app-Stop en rond de provider-geplande `TIMEEND`; hervat na onzekere Stop zodra read-back/recovery beëindiging bevestigt.
- Koppel historie op providerproduct en `atn_id`. Verwerk afgeronde `TIMESTART`, `TIMEEND` en bruikbare `COST` idempotent; gebruik backoff bij vertraagde zichtbaarheid en markeer administratie als onvolledig als retries uitgeput zijn.
- Leg beëindigde actie en reconciliatietaak atomair vast wanneer ze samen worden verwerkt. Herhaalde Stop/recovery mag geen dubbele taak maken.
- Laat reconciliatie na `Completed` toe in execution policy, processor en recovery. Stop-claim behoudt reconciliatiewerk; recovery kan ontbrekend werk idempotent herbouwen.
- Test dat reconciliatietaken na `Completed` worden verwerkt en dat herhaalde Stop/recovery geen dubbele taak oplevert.

**Gate 5c:** crash/restart- en duplicate-tests bewijzen dat reconciliatie niet verloren gaat, ook niet na Visit-voltooiing, en dat retries geen dubbele verwerking geven.

### 5d. Saldo, rapportage en waarschuwingen

- Bereken budget en urensaldo op basis van betaalde tijd binnen provideractie-intervallen; tel gratis gaten en vóór start geannuleerde acties niet mee.
- Houd `Visit.ActualEndAt` op het werkelijke scheduler-afrondmoment. Laat historiecorrecties doorwerken in saldo en rapportages.
- Verstuur bij correctie geen budgetwaarschuwingen dubbel. Reeds verzonden waarschuwingen blijven als historische berichten staan, ook als gecorrigeerd gebruik lager is.
- Verwerk ontbrekende historie na alle retries met de afgesproken fallback: laatst opgeslagen waarden behouden en administratie als onvolledig markeren.
- Test correcties van starttijd, eindtijd en kosten na Visit-afronding en verifieer dat saldo en rapportages worden bijgewerkt. Test dat correcties geen dubbele budgetwaarschuwingen versturen en eerder verzonden waarschuwingen behouden blijven.

**Gate 5d:** alle scenario's uit SR-002 zijn afgedekt, inclusief meerdere acties met gratis gat, annulering vóór start, vertraagde historie, Stop-retry, correctie na voltooiing, waarschuwing-idempotentie en de verschillen tussen Visit-eindtijd en actie-eindtijden.

## Fase 6 — Integrale acceptatie en uitrol

1. Voer na alle gates de volledige relevante testsuites uit voor Domain, Application, Infrastructure en IntegrationTests; voer daarnaast de solution build en migrationscontrole uit.
2. Test de volledige stop-, restart- en recoveryflows in TwoParkMock, inclusief visibility delay en herhaalde retries.
3. Voer live-providerchecks alleen uit met het expliciete testaccount en de testactie. Stopacties zijn destructief voor die provideractie; leg response, read-back en historie geanonimiseerd vast.
4. Controleer vóór productie-uitrol dat er geen onopgeloste provideractie of onverwacht `Claimed` werk is en dat de benodigde schemawijzigingen correct zijn toegepast. Migratie van bestaande administratie wordt niet uitgevoerd.
5. Rol na acceptatie handmatig uit als één API/schedulerinstance zonder overlap. Controleer na uitrol de Visit-status, openstaande provideracties, due/pending/claimed werk en SR-002 reconciliatie-uitkomsten voordat de wijziging als afgerond geldt.

**Eind-gate:** alle fasespecifieke gates zijn groen, integrale tests slagen, database-upgrade is gecontroleerd en operationele steekproeven tonen geen achtergebleven dekking, dubbel providerwerk of verloren reconciliatietaken.

## Uitrol bij problemen

- Bij een gatefout: stop vóór de volgende fase; houd de betreffende wijziging klein en herstel eerst de regressie in dezelfde fase.
- Bij een productieprobleem: stop verdere uitrol en los het probleem op voordat de wijziging als afgerond geldt. Terugzetten naar een vorige applicatieversie en migratie van bestaande administratie zijn buiten scope.
- Hervat schedulerverwerking pas nadat de actieve instance en de status van provideracties/claims zijn gecontroleerd. Voer onzekere provider-mutaties niet opnieuw uit; gebruik read-back/recovery.
