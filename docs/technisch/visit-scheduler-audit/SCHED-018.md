# SCHED-018 — Visit scheduler observability / audit trail

**Status:** ✅ Acceptatie afgerond op 4 oktober 2026; migraties gevalideerd op een wegwerp-PostgreSQL-database
**Prioriteit:** middel/hoog  
**Raakt:** alle schedulerflows, beheerdiagnostiek en troubleshooting.

## Huidige status — 4 oktober 2026

SCHED-001 t/m SCHED-017 zijn geïmplementeerd en opnieuw geverifieerd. De scheduler is single-instance en voert startup recovery uit voordat nieuwe workclaims beginnen.

De huidige duurzame records zijn correctness-state, geen complete eventhistorie:

- `Visit` bewaart de huidige status/health en actuele gewenste/eindmomenten;
- `VisitSchedulerWork` bewaart de huidige workstatus, DueAt, AttemptCount en actuele claim/completionvelden; defer, release en statusovergangen overschrijven die toestand;
- `ProviderOperation` bewaart de huidige operationstatus, totaal aantal pogingen en laatste foutcode, niet de afzonderlijke pogingsovergangen;
- `ProviderParkingAction` bewaart de huidige providerstatus en bekende actuele/planningsmomenten;
- `VisitEndTimeChange` bewaart al actor, vorige/gevraagde eindtijd, resultaat en tijdstip;
- `AdminAuditEvent` is voor muterende beheeracties; systeemdiagnostiek toont alleen actuele scheduleraggregaten.

De beheer-Visit-detailpagina bevat een chronologische schedulerhistorie per Visit, gegroepeerd per poging, met uitklapbare technische details. De tijdlijn combineert persistente Visit-, schedulerwork-, provideroperation- en provideractiontransities met de bestaande `VisitEndTimeChange`-historie. Vanuit een event zijn work-, operation- en actionrecords bereikbaar. `reconciliation_started` wordt als afzonderlijke timeline-stap vastgelegd vóór het resultaat, binnen dezelfde transactie. De reason-catalogus is beoordeeld: belangrijke Stop-, DesiredEndAt-, terminal-work-, retry- en recoverybeslissingen gebruiken specifieke codes; onbekende provideruitkomsten bewaren uitsluitend vaste foutcodes, geen exceptiontekst of providerpayload.

De volledige migratieketen is op de geïsoleerde Testcontainers-PostgreSQL toegepast. De suite slaagt met **430/430 .NET-tests**, **16/16 frontendtests** (inclusief een render-test voor work-, operation- en action-links) en een geslaagde productiebuild. Er is geen migratie op een blijvende database toegepast.

## Bevestigde functionele scope
### Tijdlijndoel
SCHED-018 levert een **read-only, beheerder-only tijdlijn per Visit** op voor operationele diagnose. De beheerder moet hiermee zonder losse serverlogs kunnen beantwoorden:

1. Wat gebeurde er, en in welke volgorde?
2. Welke schedulerbeslissing werd genomen en waarom?
3. Wat was het resultaat, en wat gebeurde er daarna bij retry, recovery of reconciliation?
4. Welke Visit-, work-, provideroperation- en provideractionrecords horen bij die gebeurtenis?

De tijdlijn registreert betekenisvolle beslissingen en resultaten, niet iedere pollingcyclus of interne logregel. De eerste implementatie legt onder meer vast:

- Visit-lifecycle- of healthtransitie die operationeel relevant is;
- schedulerwork aangemaakt/herbouwd, geclaimd, uitgesteld, vrijgegeven voor retry, voltooid of geannuleerd, met veilige reden en eventuele nieuwe DueAt;
- provideroperation-poging gestart en resultaat: geslaagd, mislukt of onbekende uitkomst;
- reconciliation/recovery gestart en resultaat, wanneer die de Visit- of workstatus wijzigt;
- discrepancy of overgang naar `AttentionRequired`;
- relevante Visit-mutatie, zoals DesiredEndAt-wijziging, via de reeds bestaande `VisitEndTimeChange`-historie.

De laatste regel is een presentatie-eis, geen voorstel om dezelfde eindtijdwijziging dubbel op te slaan. Provideraction- en provideroperationdetails blijven uit hun bestaande records afkomstig; een timeline-event legt alleen betekenisvolle overgang/oorzaak en correlatie vast.

### Privacy, toegang en replay
### Operationele grenzen

- Alleen beheerders zien de tijdlijn; dit is geen gebruikersactiviteitsoverzicht.
- De tijdlijn is aanvullend en read-only; bestaande Visit-, provider- en schedulerrecords blijven de bron voor actuele state en transacties.
- Geen ruwe exceptiontekst, providerpayloads, credentials, tokens of onnodige persoonsgegevens opslaan.
- Herhaalde recovery/replay mag geen dubbele betekenisvolle gebeurtenis tonen.
- Geen events voor idle polls of read-only checks die geen beslissing, stateverandering of relevante uitkomst opleveren.

### Bewaarbeleid en groepering

- De tijdlijn is alleen zichtbaar voor beheerders.
- De tijdlijn blijft bestaan zolang de bijbehorende Visit-historie bestaat; er is geen aparte retentie.
- De tijdlijn groepeert samenhangende scheduler-/providerstappen per poging; technische details zijn per groep beschikbaar zonder elk intern moment als gelijkwaardige hoofdmelding te tonen.

### Projectie van bestaande eindtijdhistorie
De timeline projecteert bestaande eindtijdhistorie naast nieuwe persistente transitie-events; bestaande gebeurtenissen worden niet nogmaals als audit-event opgeslagen.
## Functionele acceptatievoorbeelden

- Een normale Visit toont in volgorde start, relevante coverageplanning/providerresultaten en afronding.
- Een onbekende provideruitkomst toont poging, reconciliation/read-back-resultaat en vervolgactie, zonder blind retry te suggereren.
- Uitgesteld of geannuleerd schedulerwork toont de reden en, waar van toepassing, de nieuwe DueAt.
- Een restart/recovery is zichtbaar wanneer die work of Visit-state herstelt; herhaalde recovery voegt geen duplicaten toe.
- Een beheerder kan vanuit een timeline-item doorklikken naar de gecorreleerde bestaande work-, operation- of actiongegevens.
- Een DesiredEndAt-wijziging toont actor, oud/nieuw en resultaat precies eenmaal.

De doelgroep, retentie en poging-gebaseerde groepering zijn bevestigd. De evententiteit, transactionele capture, beheer-Visit-detailprojectie en gegroepeerde timeline-UI zijn geïmplementeerd; de acceptatie is afgerond en het bewijs staat hieronder.

## Doel

Per Visit een persistente, chronologische scheduler/audit-timeline beschikbaar maken waarmee achteraf betrouwbaar kan worden vastgesteld **wat de scheduler heeft gedaan, waarom dat gebeurde en welke provider-/Visit-state daarbij hoorde**.

Dit is nadrukkelijk iets anders dan gewone applicatie-/structured logging. Serverlogs blijven bedoeld voor technische runtime-diagnostiek; de Visit scheduler audit trail wordt een domeingerichte historie die aan één Visit gekoppeld blijft.

## Waarom nu

De schedulerstate-machine en reliability-hardening voor SCHED-001 t/m SCHED-017 zijn geïmplementeerd en opnieuw geverifieerd. SCHED-018 bouwt daarop voort met persistente, Visit-gebonden transitie-events en een beheerweergave.

`VisitSchedulerAuditEvent` is het append-only model voor state-transities. Capture wordt atomair met de gewijzigde records opgeslagen; `VisitEndTimeChange` wordt alleen bij het opvragen geprojecteerd.

## Acceptatiecriteria

De bevestigde acceptatiecriteria zijn:

- persistent en restartbestendig zijn;
- chronologisch per Visit opvraagbaar zijn;
- betekenisvolle domein-/schedulertransities vastleggen, niet iedere interne logregel;
- scheduler work, provider operations en provider actions kunnen correleren;
- retries en reconciliation verklaarbaar maken;
- voldoende context bevatten om een incident te reconstrueren zonder losse serverlogs te combineren;
- geen secrets, providercredentials of onnodige persoonsgegevens opslaan;
- geschikt zijn om later in beheer als Visit-timeline te tonen.

## Aanvullende eventcategorieën

De eventcapture legt de volgende betekenisvolle transities vast; read-only checks en idle polls produceren geen events:

- scheduler-work aangemaakt, geclaimd, uitgesteld, voltooid of geannuleerd;
- terminale Visit-boundary gepland/gewijzigd/bereikt;
- continuation gepland;
- provideraction voorbereid / scheduled / active / gestopt / completed;
- manual Stop die schedulerwerk of providerdekking beïnvloedt;
- `DesiredEndAt` wijziging met vervangen/geannuleerd work;
- gratis/betaald overgang wanneer dit scheduleractie veroorzaakt;
- harde policygrens bereikt;
- retry/reschedule inclusief reden;
- provider mutation met unknown outcome;
- reconciliation gestart en resultaat;
- startup recovery / scheduler rebuild;
- discrepancy / `AttentionRequired`;
- automatische Visit-finalization.

## Huidig eventmodel

Elk event bevat `VisitId`, tijdstip, bronsoort/-id, eventtype, reden, groepssleutel, optioneel attemptnummer, details en een unieke eventkey. Broncorrelatie staat in `SourceId` en de begrensde JSON-details:

- Work-, operation- en action-id's zijn logische correlaties, geen aparte foreign-keykolommen op het event. De bron-id, groepssleutel en beperkte JSON-details bevatten de relevante verwijzingen en state.

Retentie en groepering zijn bevestigd. Capture voegt events uitsluitend toe binnen dezelfde SaveChanges-transactie; de database dwingt idempotentie af met de unieke `EventKey`.

## Beoordeelde acceptatievragen

1. **Reason-catalogus:** voldoende specifiek voor belangrijke defer-, cancel-, retry- en recoverybeslissingen; regressies onderscheiden onder meer ontbrekende provideractie, externe Stop, status-/eindtijdmismatch, policy-release en terminale herplanning.
2. **Unknown/reconciliation/recovery:** read-back-uitkomst, afzonderlijke startstap, retry of resultaat en startup recovery zijn chronologisch en replay-idempotent getest.
3. **Integrale acceptatie:** admin-autorisatie, work/operation/action-correlatie, frontendgroepering en gerenderde bronlinks zijn getest; de volledige EF-migratieketen is toegepast op de wegwerp-PostgreSQL-fixture.

## Verificatiecriteria

SCHED-018 kan pas naar ✅ wanneer minimaal bewezen is dat:

1. iedere belangrijke schedulerflow een begrijpelijke Visit-timeline oplevert;
2. retries/recovery/reconciliation causaal te reconstrueren zijn;
3. events niet dubbel ontstaan door replay/idempotency;
4. de audit trail restartbestendig is;
5. beheer de historie per Visit kan raadplegen;
6. de audit trail geen secrets of onnodige gevoelige data bevat.

## Afronding


De doelgroep, eventgranulariteit en retentie zijn bevestigd. De resterende reason-, correlatie-, autorisatie-, privacy-, migratie- en UI-controles zijn afgerond; SCHED-018 is voor deze iteratie geaccepteerd. Een migratie naar een blijvende omgeving blijft onderdeel van de normale deployment, niet van deze verificatie.
