# Functioneel ontwerp — Provider History & Reconciliation

Status: ontwerp voor V1; technische aannames moeten worden gevalideerd op echte 2Park-historydata.
Branch: `feature/provider-history-reconciliation`
Datum: 2026-10-08

## Doel
Eenmalig bestaande 2Park-parkeeractiehistorie importeren en daarna provideracties periodiek opnieuw vergelijken en waar nodig corrigeren. Zowel acties vanuit de Parkeren App als acties die buiten de app in 2Park zijn aangemaakt moeten zichtbaar zijn. Het verbruik van het productgebonden jaarbudget (in Oss: 1500 betaalde parkeeruren) moet alle relevante provideracties omvatten.

## Reikwijdte
- Eenmalige, door beheerder te starten historische import.
- Handmatig en periodiek synchroniseren van 2Park-acties.
- Eén centraal beheerscherm voor **alle** provideracties, met zoeken, filters, detailinformatie, synchronisatiestatus en wijzigen van gebruikerstoewijzing.
- Kentekenregistratie en optionele toewijzing van historische provideracties aan app-gebruikers.
- Correct totaalverbruik en budgetwaarschuwingen, inclusief acties zonder Visit.
- Reconciliation van gewijzigde providergegevens, met audit trail en conflictbehandeling.
- Ondersteuning van meerdere providerproducten en kalender-/productgebonden parkeerregels.

## Buiten scope
- Import van financiële transacties, saldoboekingen en validatie van het 2Park-saldo; dit is een afzonderlijke toekomstige feature.
- Het reconstrueren van oude Visits alsof onze app ze had uitgevoerd.
- Het achteraf afvuren van historische pushmeldingen.
- Het automatisch wijzigen of starten/stoppen van 2Park-acties als gevolg van een historie-import.

## Bronnen van waarheid
- 2Park is leidend voor externe action-id, providerstatus, feitelijke tijden en providerkosten.
- De Parkeren App is leidend voor Visit, gebruikerstoewijzing, beleidsbeslissingen, audit en notificaties.
- Een geïmporteerde actie mag geen gefingeerde Visit of provideroperatie krijgen.

## Import- en toewijzingsregels
1. Identificeer provideracties primair op provider + product + externe action-id. Reeds bekende acties worden vergeleken/bijgewerkt, niet gedupliceerd.
2. Als het kenteken onbekend is, maak een Vehicle aan zonder actieve gebruikersrechten; bewaar de importherkomst.
3. Als een bestaand kenteken aan exact **één** app-gebruiker is gekoppeld, wijs de historische actie automatisch toe met toewijzingsbron `Inferred`.
4. Bij nul of meerdere gekoppelde gebruikers blijft de actie `Unassigned`.
5. De beheerder mag een provideractie expliciet aan een gebruiker toewijzen of een toewijzing verwijderen: bron `ManuallyAssigned` respectievelijk `Unassigned`.
6. Voor door de app uitgevoerde acties is de initiële gebruikerstoewijzing `Confirmed`.
7. Veranderende Vehicle/UserVehicle-koppelingen wijzigen bestaande historische gebruikerstoewijzingen nooit automatisch.
8. Een handmatige toewijzing wordt nooit door synchronisatie overschreven.
9. Alle toewijzingswijzigingen moeten achteraf traceerbaar zijn (oud, nieuw, bron, actor, tijdstip).
10. Ontbrekend kenteken of andere onvolledige providerinformatie mag geen foutieve automatische gebruikerskoppeling veroorzaken.

## Beheerpagina Provideracties
- Toon alle acties: managed, imported en extern waargenomen.
- Kolommen: datum, kenteken, product, start/einde, duur, kosten, status, herkomst, gekoppelde gebruiker, toewijzingsbron en sync-/conflictstatus.
- Filters: datum/periode, kenteken, gebruiker, product, status, oorsprong, (niet-)toegewezen, toewijzingsbron, discrepanties.
- Detailweergave met originele provideridentiteit, tijdstippen, status, kosten, gekoppelde Visit indien aanwezig en audit.
- Acties: gebruiker wijzigen/ontkoppelen, handmatige sync starten, conflict bekijken. Wijziging van gebruiker verandert geen providergegevens.
- Laat voortgang/resultaat van import zien: bekeken, toegevoegd, bijgewerkt, ongewijzigd, onvolledig, conflicten en fouten.

## 1500-urenbudget
- Iedere unieke, voor het budget relevante provideractie telt mee, ongeacht herkomst, Visit of gebruikerstoewijzing.
- Gebruik feitelijke start- en eindtijd en de op dat moment geldende betaalvensters/feestdagen en parkeerregels van het product.
- Voorkom dubbeltelling tussen geïmporteerde en bestaande managed acties.
- Acties over een jaar- of budgetperiodegrens worden naar relevante periodes gesplitst.
- Lopende acties vragen een aparte voorlopige berekening; onvolledige tijden worden zichtbaar gemarkeerd en niet stilzwijgend geschat.
- Het wijzigen van een gebruikerskoppeling beïnvloedt enkel rapportage per gebruiker, nooit het totale productbudget.
- Controleer met echte providerdata of de lokale rekensystematiek overeenkomt met de 2Park-jaarurenadministratie.
- Herbereken budget en waarschuwingen na import of correctie; voorkom een stroom aan retrospectieve meldingen.

## Synchronisatiegedrag
- Eerste volledige import op uitdrukkelijke beheeractie, niet automatisch bij het configureren van de provider.
- Handmatige herhaling is veilig en idempotent.
- Daarna incrementeel/periodiek met overlap voor later gewijzigde status, definitieve tijden en kosten.
- Lopende actie-/Visit-recovery behoudt prioriteit; conflicten worden gesignaleerd in plaats van blind overschreven.
- Externe acties krijgen een eigen lokale registratie; open discrepanties mogen worden afgesloten zodra veilig verwerkt.
- Een falende batch mag eerder succesvol verwerkte pagina's niet ongedaan maken of data dupliceren.
- Syncgeschiedenis/laatste succesvolle controle en fouten zijn zichtbaar in beheer.

## Acceptatiecriteria
- Tweede identieke import levert geen nieuwe duplicaten op.
- Onbekend kenteken wordt zonder parkeerrechten vastgelegd.
- Exact één huidige gebruiker geeft `Inferred`; nul of meerdere geeft `Unassigned`.
- Een handmatig aangepaste gebruiker blijft behouden na herhaalde synchronisatie.
- Alle relevante provideracties, ook zonder Visit, tellen exact één keer mee voor het jaarbudget.
- Beheer kan alle acties filteren, details bekijken en toewijzingen aanpassen.
- Niet-herleidbare of tegenstrijdige data worden zichtbaar gesignaleerd, niet stilzwijgend geraden.
- Financiële transactiehistorie en saldo-validatie blijven buiten de implementatie.
- De volledige feature blijft op de feature branch tot afsluitende validatie; daarna één PR naar `develop`.

## Nog te valideren
- Echte `get_action_history.json` velden: `MBR_IDENT`, `LOCATION`, status, kosten/valuta, tijden en pagination.
- Of externe action-id globaal uniek is of per product moet worden gescopeerd.
- Wat 2Park precies als betaald uur voor de 1500-urenlimiet registreert.
- Gedrag van historie bij lopende/gewijzigde acties en bewaartermijn van providerhistorie.
