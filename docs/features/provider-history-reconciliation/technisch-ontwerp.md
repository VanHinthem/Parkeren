# Technisch ontwerp — Provider History & Reconciliation

Status: ontwerp; verificatie echte 2Park-responses vereist vóór implementatie.
Branch: `feature/provider-history-reconciliation`
Datum: 2026-10-08

## Bestaande code en impact
- `TwoParkProvider : IParkingProvider, IProviderActionHistoryReader` leest `get_action_history.json` met pagina's tot 10 records; DTO `ProviderActionHistoryRecord` bevat externe id, status, feitelijke start/einde, kosten, valuta.
- `TwoParkActionHistoryParser` parseert momenteel geen kenteken of locatie uit history; controleer de ruwe response vóór het uitbreiden van DTO's.
- `ProviderParkingAction` heeft optionele `VisitId`, provider-id/product, planned/actual timestamps, status, history-status en kosten. Huidige mutators volgen een strikte operationele lifecycle; import mag die lifecycle niet nabootsen.
- `VisitRecoveryService` detecteert `ExternalProviderAction`, ontbrekende provideracties en status-/eindtijdafwijkingen. `ProviderDiscrepancyService` bewaart observaties.
- `RealizedParkingBudgetUsageCalculator` telt momenteel alleen provideracties van completed Visits. `BudgetWarningService` gebruikt dat pad: imported actions zonder Visit vallen er momenteel buiten.
- `Vehicle` normaliseert kentekens naar hoofdletters zonder leestekens. `UserVehicle` beheert koppelingen; importer mag geen parkeerrechten toekennen.

## Datamodel (voorgestelde uitbreiding)
### ProviderParkingAction
- Behoud `VisitId?`.
- Voeg `VehicleId?`/canoniek kenteken toe afhankelijk van bestaande persistencemapping; acties mogen niet hun kenteken verliezen wanneer voertuigen later wijzigen.
- Voeg `AssignedUserId?` toe, **los van** `Visit.UserId`.
- `AssignmentSource`: `Confirmed`, `Inferred`, `ManuallyAssigned`, `Unassigned`.
- `Origin`: `Managed`, `Imported`, `External`.
- `FirstObservedAt`, `LastSyncedAt` of vergelijkbare metadata.
- Hanteer één semantiek voor externe providerstatus en actual timestamps; behoud de exacte provider-id.
- Unieke database-constraint op (provider-identiteit, product-identiteit, externe action-id), rekening houdend met null bij nog niet aangemaakte provideracties.
- Voeg een expliciete, gecontroleerde importer/updater toe voor historische acties; gebruik geen `MarkStarting` of provider-mutatieflow voor import.
- Voor managed acties met reeds `Reconciled` history is een afzonderlijke gecontroleerde correctieroute nodig: `ApplyProviderHistory` eist nu `Pending`.

### Vehicle
- Leg importherkomst en eventueel registratiestatus vast als het bestaande Vehicle-statusmodel hiervoor onvoldoende is.
- Nieuwe import-Vehicles mogen niet impliciet UserVehicle-toegang of parkeerautorisatie krijgen.

### ProviderHistorySyncState (nieuw)
- Provider/productidentiteit, laatst succesvol synchronisatietijdstip, checkpoint/pagination-informatie, status, foutcontext.
- Behandel provider-indexen als mogelijk veranderlijk; gebruik overlappende rereads en herstartbare paging.

### ProviderActionAssignmentAudit (nieuw)
- Provideractie, oude/nieuwe UserId, oude/nieuwe bron, actor, changed-at. Beheeracties auditten, ook ontkoppelen.

### Eventuele sync-run historie
- Per run mode (bootstrap/incremental/manual), start/einde, aantal gelezen/ingevoegd/bijgewerkt/overgeslagen/fout, resultaat.
- Geen geheimen of providercredentials in logs of audit.

## Services en verantwoordelijkheden
- `IProviderActionHistoryReader`: paginagewijs historische brondata lezen (uitbreiden na echte responsevalidatie).
- `ProviderHistorySynchronizationService`: pagina's ophalen, validatie, upsert, checkpoint en resultaat.
- `ProviderActionAssignmentService`: Vehicle-resolutie, aantal gekoppelde gebruikers bepalen, bij eerste import auto-`Inferred` bij exact één gebruiker; bestaande `ManuallyAssigned` behouden; afzonderlijke beheer-API voor aanpassen.
- `ProviderHistoryCorrectionService` of expliciet domeincommando: feitelijke providergegevens van reeds bekende acties bijwerken met audit en bescherming voor in-flight operations.
- `ProviderDiscrepancyService`: onopgeloste verschillen bijhouden en resolved markeren wanneer veilig gecorrigeerd.
- `RealizedParkingBudgetUsageCalculator`: van Visit-gecentreerde selectie naar unieke provideractie-gecentreerde berekening per product/periode.
- `BudgetWarningService`: herberekening uitvoeren bij sync zonder retrospectieve notificatiestorm; ook direct starten/stoppen blijft werken.

## Matching en integriteit
1. Identiteit primair provider + product + externe action-id, niet fuzzy timestampmatching.
2. Gebruik bestaande `ProviderActionMatchPolicy` uitsluitend waar toepasselijk bij bestaande active-recovery. De huidige 5-secondenmarge is een engineering margin, geen gemeten provider-SLA.
3. Normaliseer kentekens identiek aan bestaande `Vehicle.NormalizeLicensePlate`.
4. Resolve Vehicle met canoniek kenteken; maak ontbrekend Vehicle aan zonder `UserVehicle`-koppeling.
5. Bij initieel toewijzen: tel daadwerkelijk gekoppelde gebruikers; 1 = `Inferred`, anders `Unassigned`.
6. Een bestaande handmatige toewijzing wordt niet door herimport overschreven; verandering in voertuigrelaties herclassificeert historische acties niet.
7. Maak upsert transactioneel met unique key en concurrencyafhandeling, zodat gelijktijdige worker en beheer-sync geen duplicaten maken.
8. Een provideractie zonder betrouwbare tijden of product-/identiteitsinformatie wordt als incomplete/conflict bewaard of geparkeerd voor herstel; nooit fictieve tijden invullen.
9. Geen provider-side wijzigingen, Visit- of scheduler-creatie door bootstrap-import.

## Budget en rapportage
- Bereken paid segments uit provideracties op basis van werkelijke tijdsintervallen, productregels en begrotingsperiode.
- Vermijd de bestaande filter `Visit.Status == Completed` als toegangspoort voor gerealiseerde uren.
- Besteed aandacht aan tijdzones, DST, perioden over middernacht en jaargrenzen, aansluitende acties en onvolledige history.
- Neem bedragen niet als vervanging voor financiële transactiehistorie of saldo-boekingen.
- Na import/herstel verbruik en waarschuwingen herberekenen met auditbare mutaties.

## Reconciliation en scheduler
- Huidige actieve Visit recovery behoudt de exclusieve verantwoordelijkheid voor in-flight provider-mutations en veilige statusovergangen.
- Background history sync leest; schrijft alleen gecontroleerde historische velden waar operationele lock/invariants dat toestaan.
- Onbekende externe actieve acties blokkeren zo nodig nieuwe starts via provider-capacity controle; geen automatische Visit aanmaken.
- Resolve `ExternalProviderAction` discrepanties pas als actie duurzaam bekend en correct verwerkt is.
- Werk in kleine pagina's, annuleerbaar, herstartbaar, met retry/backoff en zichtbaar resultaat.

## API en beheer
- `GET /admin/provider-actions` met paginering, sorteren en filters.
- `GET /admin/provider-actions/{id}` detail, gekoppelde Visit en audit.
- `PUT /admin/provider-actions/{id}/assignment` voor gebruiker instellen/ontkoppelen, admin-only, met audit.
- `POST /admin/provider-history/sync` admin-only voor gecontroleerde handmatige sync; async job met statusroute waar nodig.
- `GET /admin/provider-history/sync-status` inclusief laatste sync en foutoverzicht.
- Autorisatie, inputvalidatie en veilige concurrency vereist; exacte routes afstemmen op bestaande API-conventies.

## Tests
- Unit: identity/idempotentie, Vehicle-normalisatie, 0/1/meerdere gebruikers, handmatige override, budgetsegmentatie.
- Integratie: gelijktijdige upsert, referentiële integriteit, transactieherstel, pagination/checkpoint, budget zonder Visit, correctie managed actie.
- Mock: representatieve history met kenteken/product/locatie zodra echte payload bekend is.
- Regressie: start/stop/scheduled action recovery, Visit state machine, provider discrepancy detection, notificaties, 5/5 provider capacity.
- E2E: bootstrap -> reimport -> correctie -> gebruiker wijzigen -> herimport -> budget gelijk.

## Werkafspraak
- Eén feature branch `feature/provider-history-reconciliation` vanaf `develop`.
- Eerste commit bevat functioneel en technisch ontwerp.
- Daarna Epic en deelissues op GitHub; implementatie per kleine logische conventional commit op dezelfde feature branch.
- CI na iedere stap; gebruiker meldt groen of deelt foutieve run.
- Geen tussentijdse merges/PR's naar `develop`; pas één finale PR als de volledige feature af en getest is.

## Open onderzoek vóór code
- Live 2Park-history payload en gegarandeerde velden, maximale paginagrootte, indexstabiliteit en history-retentie.
- Provideractie-identiteit en feitelijke eindtijd wanneer extern is gestopt.
- Bron van waarheid en definitie voor 1500 betaalde uren per budgetperiode/product.
- Database-indexen/EF mapping en exacte impact op huidige query's voor dashboards, budget en capaciteit.
