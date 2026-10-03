# Scheduler restpunten

Het gecontroleerde uitvoeringsplan voor SR-001 t/m SR-005 staat in [Scheduler restpunten — implementatieplan](scheduler-restpunten-implementatieplan.md).

## SR-001 — Verlengingscontrole kan verouderde Visit-status lezen

**Status:** werking besproken; technische oplossing en reproductie nodig\
**Impact:** een provideractie kan mogelijk worden verlengd nadat Stop is gestart of de Visit niet meer gezond is.

### Situatie

De scheduler laadt en trackt de Visit in een scoped `ParkerenDbContext`. De `ProviderExtendMutationGuard` neemt later de Visit-lock, maar leest Visit en provideractie met trackingqueries in dezelfde context. Entity Framework kan daardoor de al gevolgde instanties teruggeven zonder de actuele databasewaarden opnieuw te laden. De guard kan dan een oude status zoals `Active` en `Healthy` zien en de verlenging toestaan.

### Gewenste werking

Lees onder de Visit-lock de actuele status en provideraction-state opnieuw, bijvoorbeeld met no-tracking scalarprojecties of expliciete reloads. De guard moet een verlenging blokkeren als Stop of een healthwijziging inmiddels is vastgelegd.

### Verificatie

Voeg twee afzonderlijke integratietests toe:

- De scheduler laadt eerst Visit en provideractie; een tweede scope start daarna Stop; de verlengingsguard/executor mag geen `ExtendAction` naar de provider sturen.
- De scheduler laadt eerst Visit en provideractie; een tweede scope wijzigt alleen de Visit-health; de verlengingsguard/executor mag geen `ExtendAction` sturen.

De health-only test is nodig omdat Stop via andere controles de verlenging al kan blokkeren en zo een fout in de stale-state controle kan verbergen. Een verse statuslezing voorkomt stale trackingdata, maar op zichzelf niet dat Stop ná de guardcontrole begint: de Visit-lock wordt vrijgegeven voordat de providerrequest plaatsvindt. Test en ontwerp moeten die resterende race expliciet meenemen.

### Afgesproken werking bij gelijktijdige Stop

Als de providerverlenging al onderweg is wanneer de gebruiker Stop kiest, wordt Stop vastgelegd en worden nieuwe verlengingen geblokkeerd. De app wacht op het resultaat van de lopende verlenging of leest de providerstatus opnieuw uit; ze neemt niet aan dat de verlenging is mislukt. Daarna stopt ze de provideractie zoals die werkelijk bestaat. De Visit wordt pas afgerond wanneer er geen actieve of ingeplande provideractie meer openstaat. De Visit-lock wordt niet vastgehouden tijdens externe provider-I/O; de technische oplossing moet deze volgorde veilig afdwingen.

## SR-002 — Visit-eindtijd en provideractiegebruik apart verwerken

**Status:** SR-002 PR1 en PR2 gemerged en gepusht naar `main` (commit `6b28c64`); productieprovideradapter wacht op bevestiging van request-/pagineringsemantiek en tijdzone\
**Afspraak in dit restpunt:** `Visit.ActualEndAt` is het moment waarop de scheduler de Visit daadwerkelijk afrondt. Budget- en urensaldo worden berekend op basis van provideractie-intervallen. De eerste actietijden komen afhankelijk van de situatie uit provider-readback, de providerplanning, de succesvolle Stop-response of recovery die een beëindigde actie bevestigt. Die waarden worden direct gebruikt en kunnen later met providerhistorie worden gecorrigeerd; er komt geen aparte voorlopigheidsmarkering op die tijden.

Een Visit kan meerdere provideracties bevatten, met eventueel een gratis gat ertussen. De Visit-eindtijd kan dus verschillen van de eindtijd van iedere provideractie. Als een beleidsgrens de Visit beëindigt, stopt de stopflow open provideracties en annuleert zij toekomstige ingeplande acties. `DueAt` blijft het tijdstip waarop schedulerwerk wordt uitgevoerd of opnieuw geprobeerd; het is niet de eindtijd van een provideractie.

Budgetgebruik en rapportages rekenen met de betaalde delen van provideractie-intervallen. De selectie van Visits en budgetperioden houdt rekening met overlap van die gerealiseerde intervallen, ook wanneer historie de actie buiten de Visit-grenzen plaatst. Een ontbrekend of ongeldig interval maakt de administratie onvolledig in plaats van dat Visit-tijden als vervanging voor bewezen providergebruik dienen.

Provideractie-eindtijden zijn niet altijd de werkelijke stoptijden. Een opgeslagen tijd kan het moment zijn waarop de applicatie de providerstop bevestigde; bij een extern gestopte of verdwenen actie kan de eindtijd ontbreken. Een actie die vóór de geplande start wordt geannuleerd, hoort nul gebruik op te leveren.

### Onderzoek naar provider-response

De twee beschikbare 2Park-integraties tonen niet aan dat het echte `stop_action.json`-antwoord een werkelijke stoptijd bevat:

- [`pyCityVisitorParking`](https://github.com/sir-Unknown/pyCityVisitorParking/blob/main/src/pycityvisitorparking/provider/2park/api.py) controleert bij `end_reservation` alleen de response-status. De geretourneerde `end_time` komt uit de meegegeven parameter; de bijbehorende test gebruikt als stop-response `{ "status": { "code": { "major": "OK" } }, "data": {} }`.
- [`2park`](https://github.com/markmooij/2park/blob/main/api_client.py) controleert na `stop_action.json` eveneens alleen of de response succesvol is en maakt `cancelled_at` lokaal met de applicatieklok. De actie-readback haalt `TIMEEND` op, maar alleen voor acties met status `ACTIVE` of `SCHEDULED`.
- De taak [`TP-005-actual-end-time`](https://github.com/markmooij/2park/tree/main/taskplane-tasks/TP-005-actual-end-time) gaat over de bij het aanmaken via de website teruggelezen boekingseindtijd; dit is geen bewijs over de response van `stop_action.json`.

Dit beschrijft wat deze clients uit de response gebruiken, niet noodzakelijk alle velden die 2Park werkelijk terugstuurt. De huidige app parseert en valideert de status uit de JSON-response via `PostAsync`, maar gebruikt geen tijdvelden uit de stopresponse. Een gerichte live-waarneming is daarom nodig voordat we concluderen of de provider een werkelijke stoptijd aanbiedt.

Voor één live `stop_action.json`-request is inmiddels deze response waargenomen:

```json
{
	"status": {
		"code": {
			"major": "OK",
			"minor": "SUCCESS"
		},
		"message": "Gelukt"
	}
}
```

Deze response bevat geen `data`-object en geen tijdveld. Dit toont aan dat deze stopresponse zelf geen stoptijd teruggaf; de readback na deze stop moet nog worden gecontroleerd om vast te stellen wat `TIMEEND` en de actiestatus daarna tonen.

Daarnaast is een live-response van `get_action_historie.json` bekeken. De response bevat `data.actions`; afgeronde records hebben onder meer `atn_state: "COMPLETED"` en parameters `TIMESTART`, `TIMEEND`, `COST` en `CURRENCY_DESC`. De gebruiker heeft bevestigd dat de afgeronde actie in het gedeelde voorbeeld dezelfde is als de gestopte testactie: het providerrecord toont de feitelijke provideractie van 30 seconden en `COST` van `0.01` in euro's. Volgens de gebruiker lag de geplande eindtijd zeker een uur later; de exacte geplande tijd is niet vastgelegd. De historie toont dus de gerealiseerde actie en niet de geplande eindtijd, en is voor deze testactie een bron voor het feitelijke actie-interval en de providerkosten.

Een aanvullend door de gebruiker aangeleverd historisch record bevestigt de concrete veldvorm. `atn_id` en `MBR_IDENT` zijn hieronder geanonimiseerd; de tijdvelden staan als `dd-MM-yyyy HH:mm:ss`-strings en `COST` als een decimale string. Dit record toont 11 seconden en `0.01` euro, maar is op zichzelf onvoldoende om de afrondingsregel van `COST` vast te stellen.

```json
{
	"atn_state": "COMPLETED",
	"atn_id": "REDACTED",
	"atn_chained": "NO",
	"atn_parameters": [
		{ "prr_label": "MBR_IDENT", "prr_value": "REDACTED" },
		{ "prr_label": "TIMESTART", "prr_value": "03-10-2026 11:36:36" },
		{ "prr_label": "TIMEEND", "prr_value": "03-10-2026 11:36:47" },
		{ "prr_label": "LOCATION", "prr_value": "OSS Zone J" },
		{ "prr_label": "COST", "prr_value": "0.01" },
		{ "prr_label": "CURRENCY_DESC", "prr_value": "\u20AC" }
	]
}
```

Een tweede aangeleverde response bevat tien acties en deze pagineringmetadata en headers (de actielijst is in dit fragment weggelaten):

```json
{
	"data": {
		"startindex": "10",
		"stopindex": "19",
		"maxindex": "20",
		"headers": [
			{ "hdr_label": "MBR_IDENT", "hdr_datatype": "LPN" },
			{ "hdr_label": "TIMESTART", "hdr_datatype": "DATETIME" },
			{ "hdr_label": "TIMEEND", "hdr_datatype": "DATETIME" },
			{ "hdr_label": "LOCATION", "hdr_datatype": "TEXT" },
			{ "hdr_label": "COST", "hdr_datatype": "MONEY" },
			{ "hdr_label": "CURRENCY_DESC", "hdr_datatype": "TEXT" }
		]
	}
}
```

De response bevat tien `COMPLETED`-acties; de getoonde `COST`-waarden zijn decimale strings met twee cijfers achter de komma en `CURRENCY_DESC` is `€`. De header bevestigt het providertype `MONEY`, maar de voorbeelden leggen niet vast hoe 2Park bedragen afrondt of opbouwt. De betekenis van `startindex`, `stopindex` en `maxindex`, en de requestparameters waarmee pagina's worden opgehaald, zijn nog niet bevestigd.

De app legt het provideractie-id vast: `TwoParkProvider` leest `atn_id` uit de actie-readback na `start_action.json`, waarna de startresultaatopslag dit bewaart als `ProviderParkingAction.ProviderActionId`. De database heeft daarop een unieke index voor niet-null waarden. Daarmee kunnen we een historie-record aan een lokale actie koppelen als `atn_id` overeenkomt. De lokale index bewijst niet dat 2Park ids over alle accounts/producten heen globaal uniek maakt; gebruik bij verificatie daarom ook het providerproduct en dezelfde testactie.

### Live-verificatie

Voer dit alleen uit met een expliciet daarvoor aangemaakte testactie/testaccount, omdat de stoprequest de provideractie beëindigt:

1. **Gedaan voor één stop:** de JSON-response van `stop_action.json` is vastgelegd; die bevat alleen de succesvolle status.
2. **Gedaan voor historie-responses:** actievelden en responseheaders zijn waargenomen. Eén response bevat tien acties met `startindex=10`, `stopindex=19` en `maxindex=20`; de exacte indexsemantiek en requestparameters moeten nog worden vastgesteld.
3. **Functioneel bevestigd voor deze testactie:** de historie toont de feitelijke actie en niet de planning; de geplande eindtijd lag volgens de gebruiker zeker een uur later dan het waargenomen actie-interval. De exacte geplande tijd en het lokale ontvangsttijdstip van de succesvolle Stop-response zijn niet vastgelegd. Controleer de actie-readback na Stop ook als de actie daar nog zichtbaar is.
4. Verifieer de betekenis en volledigheid van dezelfde tijdvelden in aanvullende scenario's, zoals een geplande beëindiging en annulering vóór start. Bewaar geanonimiseerde voorbeelden en de waarnemingen in dit restpunt.

Deze Visit-eindtijdafspraak wijkt af van bestaande functionele en technische documentatie, die bij automatische afronding `ActualEndAt` op de oorspronkelijke terminale beleidsgrens zet. De wijziging moet daarom expliciet als een functionele contractwijziging worden behandeld, niet alleen als een budgetcalculator-refactor.

### Gewenste werking en verificatie

Bereken budget en urensaldo door de betaalde tijd binnen de werkelijke intervallen van provideracties op te tellen; gratis gaten en vóór start geannuleerde acties tellen niet mee. Houd scheduler-eindtijd van de Visit, terminale beleidsgrens, `DueAt` en provideractie-intervallen afzonderlijk.

### Afgesproken richting: vertraagde providerreconciliatie

Plan duurzame reconciliatie na een Stop door de app en rond een door de provider geplande stop/eindtijd. Plan geen aparte controle na Start of geplande start. Gebruik het bestaande persistente schedulermechanisme met `DueAt` en retries, niet een losse `Task.Delay`, zodat controles na een herstart niet verloren gaan. Hiervoor is een nieuw werkitemtype en verwerking nodig; de huidige scheduler kent nog geen provideractie-reconciliatietaak.

- Bij een directe start gebruik je de starttijd uit de providerbevestiging/readback; bij een geplande start gebruik je de geplande provider-`TIMESTART`. Na een Stop door de app gebruik je als eerste eindtijd het lokale tijdstip waarop de succesvolle providerresponse is ontvangen. Als de Stop-request timed-out of anderszins geen succesvolle response oplevert, maar readback/recovery later bevestigt dat de provideractie beëindigd is, gebruik je als eerste eindtijd het lokale tijdstip waarop recovery die beëindiging voor het eerst bevestigt. Gebruik niet de tijd van de onzekere Stop-request alsof die ontvangstbevestigd was. Bij een stop die volgens de providerplanning plaatsvindt, gebruik je de provider-geplande `TIMEEND`.
- Plan de eerste historiecontrole één minuut na een succesvolle Stop door de app, of twee minuten na de provider-geplande `TIMEEND`. Als een stoprequest een onzekere uitkomst heeft, start de historiecontrole pas zodra readback/recovery bevestigt dat de actie beëindigd is; bij een definitieve stopfout die de actie actief laat, wacht je op de bestaande retry/stopflow. Haal historie op per providerproduct en `atn_id`; zodra het afgeronde record beschikbaar is, werk `TIMESTART`, `TIMEEND` en waar bruikbaar `COST` bij. Als het record ontbreekt of onbruikbaar is, probeer opnieuw na 1, 5, 15, 30 en 60 minuten; herhaal daarna ieder uur tot maximaal zes uur na de eerste controle. Markeer de actie daarna als `Incomplete`. Historiepaginering en de exacte requestparameters moeten nog worden vastgesteld.
- Zodra een provideractie beëindigd is en `ActualStartAt` en `ActualEndAt` bekend zijn, bereken je zelf de eerste `ProviderCostAmount` uit de betaalde actie-intervallen en de op die intervallen geldige tarieven voor het providerproduct. Per betaald interval is het ongeronde bedrag `duur in minuten * (uurtarief / 60)`; tel de ongeronde segmentbedragen voor de volledige provideractie op en rond het totaal eenmaal naar boven af op hele eurocenten. Gebruik hiervoor `ProviderActionCostCalculator`; de bestaande `ParkingCostCalculator` rondt afzonderlijke segmenten af en is hiervoor niet geschikt. Een later historie-record is leidend en mag zowel `ActualStartAt`/`ActualEndAt` als `ProviderCostAmount` corrigeren.
- Totdat de historie is verwerkt, blijven de hierboven afgesproken eerste tijden in gebruik; de latere providerwaarden werken ze bij. Er is geen aparte voorlopigheidsmarkering nodig. De stopresponse zelf bevat geen tijdveld, dus de initiële eindtijd bij een app-Stop is het lokale ontvangsttijdstip van de succesvolle response, niet een timestamp uit de JSON-body.

De historiecontrole moet uitvoerbaar blijven nadat de Visit `Completed` is. Dit moet op alle schedulerlagen gelden: de centrale `VisitSchedulerWorkExecutionPolicy` moet het reconciliatietype na `Completed` toestaan; de processor moet dit type vóór de gewone Visit-statusguard afhandelen; en recovery mag de taak niet annuleren vanwege de terminale Visit-status. De Stop-claimer annuleert nu alle `Pending` scheduleritems van de Visit; pas dit zo aan dat een historiecontrole behouden blijft. Als Stop of recovery desondanks vaststelt dat de controle ontbreekt, moet recovery haar veilig en idempotent opnieuw aanmaken op basis van de beëindigde provideractie. Leg de beëindigde provideractie en de bijbehorende reconciliatietaak atomair vast waar ze samen worden verwerkt, zodat een herstart de controle niet kan verliezen.

De eerste provider-readback na Start kan al een provider-eindtijd bevatten. Gebruik voor de eerste bruikbare provideractie-intervallen de bestaande `ProviderParkingAction.ActualStartAt` en `ActualEndAt`; historie corrigeert diezelfde velden. `PlannedStartAt` en `PlannedEndAt` blijven de door Parkeren aangevraagde grenzen. Voor een app-Stop is de eerste `ActualEndAt` het lokale tijdstip waarop de succesvolle Stop-response is ontvangen (de callbacktijd), niet het moment waarop de request is gestart. Als die response ontbreekt en recovery de beëindiging bevestigt, gebruikt zij het lokale tijdstip van die eerste recoverybevestiging. Bij een provider-geplande beëindiging is de provider-`TIMEEND` de eerste eindtijd. Zodra de actie beëindigd is, berekent de app de eerste kosten zelf; historie kan de intervaltijden en kosten later overschrijven. Een aparte markering dat deze eerste waarden voorlopig zijn, is niet gewenst.

Een latere historiecorrectie moet doorwerken in het gerealiseerde saldo en de rapportages. De bestaande `BudgetWarningService` beoordeelt drempels alleen bij Visit-voltooiing en slaat al gemelde drempels op. Als een correctie het gebruik verlaagt, blijven al verzonden budgetwaarschuwingen staan als historische berichten; ze worden niet ingetrokken. De gebruiker verwacht geen grote verschillen tussen de eerste waarden en de latere correctie. De implementatie moet correcties verwerken zonder waarschuwingen dubbel te versturen.

Maak de verwerking idempotent en voorkom dubbele reconciliatiewerkitems voor dezelfde provideractie, ook bij herhaalde Stop-pogingen. De functionele hoofdlijn is afgesproken; de precieze technische uitwerking is nog niet besloten. Als na alle retries geen historie-record beschikbaar komt, blijven de laatst opgeslagen tijden en kosten in gebruik en wordt de administratie als onvolledig gemarkeerd. Een later mechanisme om historie alsnog in te lezen en bestaande waarden eventueel te corrigeren valt buiten deze fallback en wordt los hiervan ontworpen.

De initiële eindtijd bij een app-Stop is het lokale ontvangsttijdstip van de succesvolle providerresponse, niet een timestamp uit de JSON-body. Als die response ontbreekt en recovery de beëindiging later bevestigt, is het lokale tijdstip van die eerste recoverybevestiging de initiële eindtijd. De historie kan beide initiële waarden later corrigeren.

### Afgesproken fallback en waarschuwingen

De fallback wanneer na alle retries geen historie-record beschikbaar komt is afgesproken: behoud de laatst opgeslagen tijden en kosten en markeer de administratie als onvolledig. Een latere historie-import en eventuele correctie van die waarden wordt als apart mechanisme behandeld. Als zo'n correctie het gebruik verlaagt, blijven al verzonden budgetwaarschuwingen staan als historisch bericht. Een aparte voorlopigheidsmarkering op de tijden zelf is niet gewenst.

Werk de functionele/technische documentatie en bestaande tests bij. Test minimaal directe versus geplande starttijd; app-Stop met succesvolle response versus provider-geplande eindtijd; Stop-timeout waarbij recovery de beëindiging later bevestigt en de recoverybevestiging als initiële eindtijd opslaat; historiecorrectie van start, eindtijd en kosten na Visit-voltooiing; meerdere provideracties met een gratis gat; een vóór start geannuleerde actie (nul gebruik); historie die pas na retries beschikbaar komt; en een providerstop die pas na meerdere pogingen slaagt. Bevestig bij die retrytest dat de Visit-eindtijd het werkelijke scheduler-afrondmoment volgt en budget alleen provideractiegebruik telt. Verifieer dat correcties saldo en rapportages bijwerken, waarschuwingen niet dubbel versturen en al verzonden waarschuwingen blijven staan wanneer gecorrigeerd gebruik daalt. Test ook dat reconciliatiewerk na `Completed` door de centrale execution policy, processor en recovery wordt toegelaten, dat een Stop-claim het werkitem behoudt, en dat recovery een ontbrekend werkitem idempotent opnieuw aanmaakt.

### Technische uitwerking 5a — voorstel ter review

- `Visit.ActualEndAt` blijft uitsluitend het scheduler-afrondmoment. `ProviderParkingAction.ActualStartAt` en `ActualEndAt` zijn de effectieve actie-intervallen voor gebruik en mogen na historie-readback worden gecorrigeerd; historie overschrijft deze waarden in plaats van een tweede set voorlopige tijden te introduceren.
- Gebruik voor provideracties dezelfde `ActualStartAt`- en `ActualEndAt`-velden voor de eerste bruikbare intervalwaarden en latere historiecorrecties; voeg geen aparte `ProviderReportedStartAt`- of `ProviderReportedEndAt`-velden toe. `PlannedStartAt` en `PlannedEndAt` blijven de door Parkeren aangevraagde grenzen.
- Bewaar gerealiseerde providerkosten op de provideractie als nullable `ProviderCostAmount`, uitgedrukt in euro. Er is geen apart valutaveld nodig; ontbrekende of ongeldige kosten maken de kostenadministratie onvolledig.
- Bewaar per provideractie een `ProviderHistoryStatus` (`NotRequired`, `Pending`, `Reconciled`, `Incomplete`), los van de status van het schedulerwerk. Gebruik `NotRequired` zolang historie nog niet nodig is, `Pending` zodra reconciliatie is ingepland of bezig is, `Reconciled` nadat een geldig record is toegepast en `Incomplete` wanneer retries zijn uitgeput of historie onbruikbaar blijkt. Een ontbrekend record houdt de eerste bruikbare tijden/kosten intact. Leid incompleetheid van de Visit-administratie af uit de acties: minstens één actie met status `Incomplete` maakt die administratie incompleet.
- Gebruik een nieuw duurzaam schedulerwerktype `ReconcileProviderAction`, gekoppeld aan de Visit en precies één provideractie via een verplichte `ProviderParkingActionId`-FK. Bewaar `AttemptCount` op het werkitem; retries gebruiken hetzelfde item. De database staat maximaal één `Pending`/`Claimed` reconciliatietaak per provideractie toe. De externe matchsleutel blijft providerproduct plus `atn_id`; vertrouw niet op globale uniciteit van `atn_id` bij de provider.
- Bereken betaald gebruik uit de doorsnede van elk effectief actie-interval met de productgebonden betaalregels. Een ontbrekend of ongeldig interval telt niet als bewezen gebruik en maakt de betreffende administratie onvolledig; gratis gaten en vóór start geannuleerde acties leveren nul gebruik op.

**Nog te bevestigen vóór Gate 5a:** hoe provider-`COST` wordt opgebouwd en afgerond, de betekenis van `startindex`/`stopindex`/`maxindex` en de volledige requestparameters voor paginering. De lokale initiële berekening is afgesproken als `duur in minuten * (uurtarief / 60)`: sommeer ongeronde segmentbedragen per provideractie en rond het totaal naar boven af op hele eurocenten. De responseheaders typeren provider-`COST` als `MONEY`; de waargenomen waarden zijn strings met twee decimalen. `CURRENCY_DESC` blijft onderdeel van de providerresponse/parserfixture, maar wordt niet apart opgeslagen omdat de valuta altijd euro is. Hardcode de nog onbevestigde provider- en pagineringsregels niet.

## SR-003 — Verlopen providerpogingen na herstart periodiek herstellen

**Status:** wijziging nodig\
**Impact:** een Visit kan in `Starting` blijven hangen nadat de applicatie tijdens een providerpoging is herstart.

### Situatie

Startup recovery laat een `InProgress` providerpoging ongemoeid zolang die binnen de veiligheidslease van vijf minuten valt. Dat voorkomt dat een nog lopende providerrequest te vroeg opnieuw wordt behandeld. Als de lease na die startup verloopt, beoordeelt periodieke recovery de poging niet opnieuw: die verwerkt `Unknown`-operaties, maar zet verlopen `InProgress`-operaties niet om.

### Gewenste werking en verificatie

Behoud de vijfminutenlease, maar laat periodieke recovery verlopen `InProgress`-pogingen daarna onder de Visit-lock opnieuw controleren en via de bestaande `Unknown`- en read-back/reconciliation-flow herstellen. Herhaal een provider-mutatie niet blind. Test dat een poging die bij startup nog binnen de lease valt, na het verstrijken van de lease alsnog wordt hersteld en de Visit niet in `Starting` blijft hangen.

Controleer ook expliciet dat vóór het verstrijken van de lease geen herstel plaatsvindt en geen tweede provider-mutatie wordt verstuurd. Periodieke recovery mag een nog lopende request van de huidige procesinstantie niet voor abandoned aanzien; eigenaarschap of een andere fencing/leasevoorwaarde moet dat onderscheid veilig maken.

## SR-004 — Mislukte release laat schedulerwerk geclaimd staan

**Status:** uitvoering gestart\
**Impact:** een tijdelijke databasefout kan schedulerwerk, waaronder een automatische Stop, geblokkeerd laten tot de volgende applicatieherstart.

### Situatie

Als verwerking faalt en ook `ReleaseFailedAsync` faalt, blijft het werk `Claimed`. De claimloop zoekt alleen `Pending` werk en startup recovery is de enige huidige route die achtergelaten claims vrijgeeft.

### Gewenste werking

Maak het vrijgeven na een verwerkingsfout robuuster, bijvoorbeeld door de release opnieuw te proberen en de fout zichtbaar te houden. Als de database tijdelijk onbereikbaar is, moet de worker het vrijgeven kunnen hervatten zodra de database terug is. Een periodiek herstel van claims vereist expliciete eigenaarschap-/leasecontrole: volgens het V1-contract mag een herstelroutine een claim van een nog actieve worker niet blind overnemen.

### Verificatie

Test dat een tijdelijke fout bij release uiteindelijk leidt tot vrijgegeven of veilig opnieuw planbaar werk, zonder dat twee workers dezelfde actieve claim verwerken. Voeg ook een test toe waarin een verouderde releasepoging een inmiddels `Completed` of `Cancelled` work-item ongemoeid laat.

## SR-005 — Recovery herkent ingeplande opvolger na gratis periode niet

**Status:** gemerged naar `main` (commit `848dc19`)\
**Ontdekt:** 3 oktober 2026

### Situatie

Bij een gratis periode kan de vorige provideractie al `Completed` zijn terwijl de volgende actie op T-5 bij de provider is ingepland met status `Scheduled`. Die ingeplande actie is geldig en hoort later de dekking voort te zetten.

Als de applicatie herstart nadat die opvolger is ingepland maar voordat de opvolgende dekking als lokaal `Active` is verwerkt, zoekt startup recovery alleen naar lokale `Active`-acties. De combinatie van een afgeronde voorganger en een ingeplande opvolger wordt daardoor niet als geldige dekking herkend. Recovery kan de Visit dan onterecht als ambigu markeren, `AttentionRequired` instellen en schedulerwerk annuleren, terwijl de geplande provideractie nog bestaat.

### Gewenste werking

Recovery moet een lokale `Scheduled`-actie expliciet bij de provider bevestigen op basis van de bekende provideractie-id en de verwachte tijden. Bij een overeenkomende providerstatus `scheduled` blijft de actie geldig en moet eventueel ontbrekend schedulerwerk worden herbouwd. Als de provider inmiddels `active` meldt, moet recovery de lokale actieovergang veilig bijwerken en de normale vervolgplanning herstellen. Bij ontbrekende of afwijkende providerinformatie blijft conservatieve afhandeling nodig.

### Verificatie

`ProviderFreeGapRecoveryTests.Recovery_rebuilds_scheduler_for_scheduled_successor_after_free_gap` voert recovery uit met een afgeronde voorganger en ingeplande opvolger. De test controleert behoud van de acties, Visit-health, terminalwerk, herstel van ontbrekend continuation-work voor een latere betaalde periode en het ontbreken van duplicaten. Afwijkende start- en eindtijden, ontbrekende read-back en een gestopte actie moeten conservatief naar review gaan.

Breid die test uit om ook te verifiëren dat:

- de Visit `Healthy` blijft;
- de bestaande Scheduled provideractie behouden blijft en niet dubbel wordt aangemaakt;
- passend vervolg- en terminal schedulerwerk behouden of herbouwd wordt;
- afwijkende of niet terugvindbare provideracties nog steeds veilig naar aandacht/review gaan.

Voeg daarnaast het scenario toe waarin de provider bij recovery de opvolger inmiddels als `active` teruggeeft, hoewel deze lokaal nog `Scheduled` staat. Verifieer dat de lokale status en vervolgplanning veilig worden hersteld zonder een tweede provideractie te maken.
