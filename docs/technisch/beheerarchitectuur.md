# Beheerarchitectuur

## Besluit

Parkeren krijgt twee verschillende beheerervaringen met een duidelijk gescheiden doel.

### Snelbeheer in de PWA

De huidige mobiele beheerpagina blijft functioneel beperkt en is bedoeld voor snelle operationele handelingen onderweg.

Doelroute:

`/snelbeheer`

De bestaande functionaliteit blijft hier beschikbaar:

- bezoeker aanmaken;
- voertuig aanmaken;
- voertuig aan een gebruiker koppelen of ontkoppelen;
- gebruiker activeren/deactiveren;
- voertuig activeren/deactiveren;
- PIN resetten;
- sessies van een gebruiker intrekken.

De huidige pagina wordt alleen nog op layout, mobile UX en consistentie met de rest van de PWA verbeterd. Er worden geen uitgebreide backendinstellingen aan toegevoegd.

Tijdens de huidige V1-ontwikkeling mag de bestaande route `/beheer` nog gebruikt worden. Zodra het volledige beheerportaal wordt geïntroduceerd, verhuist deze mobiele pagina naar `/snelbeheer`. De bottom-navigation mag voor gebruikers gewoon het label **Beheer** blijven tonen.

### Volledig beheerportaal

Het volledige beheer komt als een aparte, desktop-first beheerervaring binnen dezelfde applicatie:

`parkeren.vanhinthem.nl/beheer`

Dit is geen tweede backend. Het beheerportaal gebruikt dezelfde API, domeinlogica, persistence, authenticatie en autorisatie als de PWA, maar krijgt een eigen beheergerichte shell en informatiearchitectuur.

Het portaal is bedoeld voor volledige backendconfiguratie en beheer, waaronder uiteindelijk:

- gebruikers, voertuigen en toewijzingen;
- parkeerzones en geldigheidsperiodes;
- parkeerregels en betaalvensters;
- provider- en systeemlimieten;
- algemene en per-user policies;
- maximale Visit- en sessieduur;
- auto-extension-instellingen;
- parkeerbudgetten en waarschuwingsdrempels;
- notificatie-instellingen;
- actieve Visits en provideracties;
- discrepancies en reconciliation;
- audit- en providerhistorie;
- systeemstatus en diagnostiek;
- overige backendinstellingen die tijdens V1/V1.x beheerbaar worden gemaakt.

## UX-richtlijn

De PWA blijft primair gericht op **parkeren uitvoeren**: starten, stoppen, verlengen, actieve status, historie, meldingen, auto's en persoonlijke instellingen.

Het beheerportaal is primair gericht op **configureren, controleren en herstellen**. Daardoor hoeft uitgebreide beheerfunctionaliteit niet in een mobiele workflow te worden gepropt.

## Technische uitgangspunten

- Eén backend en één database.
- Bestaande rollen/autorisatie blijven leidend; uitgebreid beheer is alleen voor beheerders.
- Geen duplicatie van businesslogica tussen PWA en beheerportaal.
- Het beheerportaal mag een eigen frontend-shell en desktop-georiënteerde componenten krijgen.
- De route `/beheer` wordt gereserveerd voor het volledige beheerportaal.
- De huidige mobiele beheerpagina verhuist op dat moment naar `/snelbeheer`.
- De functionele scope van Snelbeheer groeit niet mee met de volledige beheerfunctionaliteit.

## Backendinventarisatie 1 oktober 2026

De eerste inventarisatie voor #95 is afgerond. De backend bevat al een groot deel van de benodigde domeinlogica; de belangrijkste ontbrekende laag is beheergerichte application/API-functionaliteit en read models.

### Reeds aanwezige beheerbasis

| Onderdeel | Backendbasis | Huidige beheer-API | Vervolg voor volledig beheer |
|---|---|---|---|
| Gebruikers | `User`, rollen, actief/inactief | ja | uitgebreid detail en audit |
| PIN/sessies | authentication + `UserSession` | ja | eventueel sessie-inzicht |
| Voertuigen/toewijzingen | `Vehicle`, `UserVehicle` | ja | uitgebreid detail |
| Globale capaciteit | `ParkingSystemSettings.MaxConcurrentVisits` | ja | opnemen in systeeminstellingen |
| Per-user capaciteit | `UserPolicyOverride.MaxConcurrentVisits` | ja | volledige policy-editor gerealiseerd in 8.5 |
| Default Visit-policy | `DefaultParkingPolicy` | nee | beheer-use-case en API in 8.6 |
| Overige user policies | `UserPolicyOverride` | ja, 8.5 | per-veld override + terug naar standaard |
| Parkeerregels | `ParkingRuleSet` | nee | versiebeheer en beheer-API |
| Betaalvensters | `PaidWindow` | nee | beheer via ruleset |
| Kalenderuitzonderingen | `ParkingCalendarException` | nee | beheer via ruleset |
| Provider-sessieduur | `MaxProviderActionDuration` | nee | beheer via ruleset |
| Continuation | `StartNewAction` / `ExtendAction` | nee | beheer via ruleset |
| Tarieven | `ParkingTariff` + kostencalculator | nee | configuratie en reporting |
| Budgetperioden | `ParkingBudgetPeriod` + usage calculator | nee | configuratie en reporting |
| Budgetwaarschuwingen | thresholds + warning service | nee | settings-API |
| Long Visit warnings | settings + scheduler | nee | settings-API |
| Actieve Visits | volledig Visit-domein | gebruikergericht | admin read model |
| Starten namens gebruiker | bestaande `StartVisit`-flow | technisch ondersteund | beheer-UI/read model |
| Stoppen namens gebruiker | bestaande `StopVisit`-flow | technisch ondersteund | beheer-UI |
| Provideracties | `ProviderParkingAction` | niet als admin read API | detail/read API |
| Provideroperations | `ProviderOperation` | niet als admin read API | detail/read API |
| Schedulerwerk | `VisitSchedulerWork` | nee | diagnostiek/read API |
| Officieel providersaldo | `IParkingProvider.GetBalanceAsync` | alleen intern/dev | admin endpoint + freshness |
| Provideracties uitlezen | `IParkingProvider.GetActionsAsync` | alleen intern/dev | reconciliation/read API |
| Recovery/reconciliation | `VisitRecoveryService` | automatisch | beheerinzage |
| Systeemstatus | `/health`, `/api/status` | beperkt | operationeel statusmodel |

### Nieuwe en resterende domeinconcepten

8.10 introduceert **ParkingProviderProduct** als lokaal catalogusrecord van een product dat door 2Park wordt geleverd. De provider-location is onderdeel van het product en geen zelfstandig door gebruiker of beheerder te kiezen parkeerzone.

Het resterende nieuwe domeinconcept binnen Fase 8 is:

1. **Persistente discrepancies** — reconciliation bestaat technisch, maar er is nog geen duurzaam discrepancy-record waarmee gedetecteerde en opgeloste afwijkingen voor #63 traceerbaar blijven.

### Configuratie versus secrets

Functionele businessconfiguratie hoort via `/beheer` onderhoudbaar te worden wanneer dat zinvol is. Infrastructurele secrets blijven buiten het beheerportaal.

Niet wijzigbaar via `/beheer`:

- database credentials;
- 2Park credentials;
- VAPID private key;
- bootstrap-admin credentials.

Het beheerportaal mag wel veilige statusinformatie tonen, bijvoorbeeld of provider/Web Push/databaseconfiguratie aanwezig en gezond is, zonder geheime waarden weer te geven.

## Informatiearchitectuur /beheer

De desktop-first beheerervaring gebruikt zes hoofdgebieden.

### 1. Overzicht

Routebasis: `/beheer`

Doel: actuele operationele toestand en aandachtspunten.

Bevat onder andere:

- actieve Visits en capaciteit;
- officieel providersaldo + timestamp/freshness;
- operationele waarschuwingen;
- provider-/reconciliationstatus;
- systeemgezondheid.

Muterende acties zijn hier beperkt tot expliciete operationele use-cases zoals een Visit starten namens een gebruiker of een actieve Visit stoppen. Het dashboard zelf wordt geen algemene configuratiepagina.

### 2. Bezoeken

Routebasis: `/beheer/bezoeken`

Pagina's:

- actieve bezoeken;
- historie;
- Visit-detail;
- verbruik en kosten.

Read-only:

- historie;
- provideractions/operations;
- toegepaste regels/policy snapshots;
- analyses, totalen en kosten.

Muterend via bestaande businessflows:

- Visit starten namens een gebruiker;
- actieve Visit stoppen;
- overige Visit-acties alleen wanneer ze expliciet door bestaande/gevalideerde domeinflows worden ondersteund.

Dit gebied realiseert primair #57, #59, #60, #61 en #62.

### 3. Gebruikers & voertuigen

Routebasis: `/beheer/gebruikers` en `/beheer/voertuigen`

Pagina's:

- gebruikerslijst;
- gebruikersdetail;
- voertuigenlijst;
- voertuigtoewijzingen;
- user policy overrides.

Muterend:

- gebruiker/voertuig aanmaken;
- activeren/deactiveren;
- koppelen/ontkoppelen;
- PIN resetten;
- sessies intrekken;
- per-user policies wijzigen.

Snelbeheer blijft dezelfde kernhandelingen mobiel aanbieden, maar uitgebreide details/policies horen uitsluitend hier.

### 4. Parkeerconfiguratie

Routebasis: `/beheer/configuratie`

Pagina's:

- parkeerregels per providerproduct;
- betaalvensters en kalenderuitzonderingen per providerproduct;
- tarieven per providerproduct;
- budgetperioden per providerproduct.

De providerproductcatalogus en defaultproductselectie staan onder `/beheer/provider`. Providerproductgegevens zoals externe product-id, categorie en location zijn read-only.

Configuratie met tijdsafhankelijk gedrag wordt versioned beheerd via `ValidFrom`/`ValidUntil`. Historische versies zijn voor audit in beginsel read-only; wijzigingen worden als een nieuwe geldigheidsversie vastgelegd in plaats van historische betekenis te overschrijven.

Dit gebied bevat ook `MaxProviderActionDuration` en `Continuation`, omdat deze bewust onderdeel zijn van `ParkingRuleSet` en niet van gebruikersbeleid.

### 5. Provider & reconciliatie

Routebasis: `/beheer/provider`

Pagina's:

- providerstatus en officieel saldo;
- actuele provideractions;
- lokale provideroperations;
- discrepancies;
- reconciliationdetail.

Providerdata is in beginsel read-only. Herstelacties worden uitsluitend via expliciete, idempotente recovery/reconciliation-use-cases aangeboden; er komt geen generieke mogelijkheid om provider- of operation-records handmatig te wijzigen.

Dit gebied realiseert primair #58 en #63 en levert providercontext voor #57/#61.

### 6. Systeem

Routebasis: `/beheer/systeem`

Pagina's:

- algemene policies/settings;
- notificatie-instellingen;
- diagnostiek;
- scheduler/pushstatus;
- audit.

Muterend:

- functionele `ParkingSystemSettings`;
- default ParkingPolicy;
- notificatie-/warninginstellingen.

Read-only:

- health/diagnostiek;
- schedulerstatus;
- pushdelivery-status;
- audit/providerhistorie;
- veilige infrastructuurstatus.

## Beheerprincipe

Het beheerportaal wordt geen generieke CRUD- of database-editor. Mutaties lopen via application/domain-use-cases zodat dezelfde validaties, locking, autorisatie, policyregels en providerlogica gelden als elders in de applicatie.

Voorbeelden:

- starten namens een bezoeker hergebruikt de bestaande `StartVisit`-flow met `OwnerUserId`;
- stoppen namens een bezoeker hergebruikt de bestaande `StopVisit`-flow;
- globale capaciteit blijft wijzigingen blokkeren wanneer actieve Visits bestaan en verlaagt te hoge user overrides;
- parkeerregels en tarieven worden tijdsgebonden/versioned beheerd;
- provider/reconciliationdata wordt niet rechtstreeks gemuteerd.

## Realisatiestatus beheerportaal — 1 oktober 2026

De beheerimplementatie is inmiddels gerealiseerd tot en met slice 8.8:

- **8.1 Beheerfundament** — desktop admin-shell op `/beheer`, admin-only routing en mobiel Snelbeheer op `/snelbeheer`;
- **8.2 Operationeel dashboard** — actieve Visits, capaciteit, aandachtstatus en starten/stoppen via bestaande Visit-flows;
- **8.3 Visit-detail & beheerhistorie** — filters, Visit-detail, provideractions/operations, policy snapshot en relevante rulesetversies;
- **8.4 Providerstatus & saldo** — officieel providersaldo met freshness/stale-semantiek en actuele provideractions;
- **8.5 Gebruikers & voertuigen** — desktop gebruikers-/voertuigenbeheer, toewijzingen, PIN/sessies en volledige per-user policy-editor;
- **8.6 Algemene policies/settings** — default user policy, globale capaciteit, Long Visit- en budgetwaarschuwingen op `/beheer/systeem`;
- **8.7 Parkeerregels** — append-only versioned rulesets met betaalvensters, kalenderuitzonderingen, feestdagenbeleid, provider-actieduur en continuation;
- **8.8 Budgetten & tarieven** — append-only budget-/tariefconfiguratie plus lokale budget- en historische kostenrapportage;
- **8.9 Analyse** — periodeaggregatie per bezoeker/kenteken met historische kosten en Visit-drill-down.

Voor user policy overrides geldt in 8.5:

- ieder bestaand overrideveld kan afzonderlijk afwijken of teruggezet worden naar de default;
- de UI toont standaardwaarde en effectieve waarde;
- `MaxConcurrentVisits` blijft begrensd door de globale capaciteit;
- een effectieve policywijziging wordt server-side geblokkeerd wanneer de gebruiker een actieve Visit heeft;
- gebruiker/voertuig deactiveren wordt eveneens geblokkeerd zolang een relevante actieve Visit bestaat;
- de bestaande Visit-policy snapshot blijft leidend voor historie.

Duur-overrides gebruiken expliciet `PolicyDurationOverrideMode` met drie toestanden: `Inherit`, `Value` en `Unlimited`. Daardoor kan een gebruiker de default volgen, een concrete eigen limiet krijgen of expliciet onbeperkt worden ingesteld, ook wanneer de default begrensd is. De nullable effectieve duur blijft uitsluitend betekenen: **geen effectieve limiet**.

Voor 8.6 geldt daarnaast:

- `DefaultParkingPolicy` is volledig beheerbaar via een expliciete administration-use-case;
- defaultwijzigingen worden per gewijzigd veld getoetst tegen actieve Visits;
- een defaultveld mag wijzigen wanneer actieve gebruikers voor dat veld een expliciete override hebben;
- de UI toont bij een blokkade de geraakte velden en het aantal actieve Visits;
- `DefaultParkingPolicy.MaxConcurrentVisits` kan niet boven de globale capaciteit uitkomen;
- verlagen van de globale capaciteit klemt zowel hogere user-overrides als de default concurrency;
- Long Visit-warningtijd, adminnotificatie, reminderinterval en budgetwaarschuwingsdrempels zijn beheerbaar;
- bestaande reeds geplande Long Visit scheduler-work behoudt zijn bestaande `DueAt`; nieuwe Visits en toekomstige reminderplanning gebruiken de actuele instellingen.

Persistente generieke audit van beheerwijzigingen wordt in 8.12 ontsloten. De huidige policy/settings-records bewaren hun `UpdatedAt`, maar #74 blijft tot die auditlaag formeel open.

Voor 8.7 geldt:

- beheerroute: `/beheer/configuratie/parkeerregels`;
- bestaande rulesetversies zijn read-only;
- wijzigingen worden uitsluitend als een **nieuwe toekomstige versie** toegevoegd;
- de vorige open-ended versie wordt atomisch afgesloten op `ValidFrom` van de nieuwe versie;
- retroactieve versies (`ValidFrom <= now`) worden geweigerd;
- rulesetversies mogen niet overlappen;
- betaalvensters binnen dezelfde weekdag mogen niet overlappen;
- kalenderuitzonderingen zijn uniek per datum binnen een ruleset;
- geen betaalvensters betekent dat reguliere tijden gratis zijn;
- `PublicHolidaysAreFree` en expliciete kalenderuitzonderingen blijven onderdeel van dezelfde versie;
- `MaxProviderActionDuration` en `Continuation` horen bewust bij de ruleset en niet bij user policies;
- een actieve Visit kan een toekomstige rulesetboundary passeren: parkeerregels zijn tijdsafhankelijk en de bestaande period-segmentatie past dan per periode de juiste ruleset toe;
- er is voor 8.7 geen schemawijziging nodig: de bestaande `ParkingRuleSet`, `PaidWindow` en `ParkingCalendarException` persistence wordt hergebruikt.

Voor 8.8 geldt:

- budgetperioden zijn expliciet begrensd en non-overlapping;
- tarieven zijn non-overlapping en historisch/versioned;
- een nieuw open-ended tarief mag de vorige open-ended versie afsluiten, maar historische tariefbetekenis wordt niet overschreven;
- budgetgebruik is lokaal afgeleid uit betaalde segmenten van completed Visits;
- budgetgebruik en providerbalans blijven verschillende bronnen: providerdata wordt niet automatisch met lokale data overschreven;
- providerbalans wordt alleen numeriek met lokale resterende tijd vergeleken wanneer de provider zelf een tijdseenheid levert;
- kostberekening gebruikt `ParkingRuleSetPeriodSegmenter`, `ParkingTimeSegmenter` en `ParkingTariffCostCalculator`;
- rapportage markeert configuratiegaten expliciet en gebruikt nooit het huidige tarief als historische fallback;
- `/beheer/verbruik` levert budget- en kosteninzage met drill-down naar Visit-detail;
- het operationele dashboard toont ook het actuele lokale budget;
- er is voor 8.8 geen schemawijziging nodig.

Voor 8.9 geldt:

- `/beheer/analyse` gebruikt dezelfde gerealiseerde cost/readmodel-logica als 8.8;
- aggregatie vindt plaats op completed Visits binnen een gekozen periode;
- bezoekersaggregatie groepeert op `Visit.UserId`, niet op voertuigbezit of `StartedByUserId`;
- daardoor blijft een gedeeld kenteken correct toewijsbaar aan de bezoeker voor wie de Visit liep, ook wanneer een beheerder die Visit namens de bezoeker startte;
- kentekenaggregatie bewaart de onderliggende Visit-usercontext;
- gearchiveerde gebruikers/voertuigen blijven via de bestaande records herkenbaar en worden als gearchiveerd gemarkeerd;
- aggregate-totalen worden niet “compleet” gemaakt wanneer historische rules/tarieven ontbreken;
- drill-down hergebruikt het bestaande `/beheer/bezoeken/{id}` Visit-detail;
- 8.9 introduceert geen nieuwe database-entiteiten of migration.

Voor 8.10 geldt:

- `ParkingProviderProduct` bewaart de door 2Park ontdekte productcontext met externe product-id, naam/categorie, location, beschikbaarheidsstatus en first/last-seen timestamps;
- providerproducten worden gesynchroniseerd en niet handmatig aangemaakt of inhoudelijk gewijzigd;
- precies één beschikbaar product kan lokaal als default voor nieuwe Visits worden gekozen;
- alleen bij de eerste synchronisatie met exact één product wordt default automatisch ingesteld;
- een verdwenen default wordt niet automatisch vervangen;
- voor V1 selecteert een gewone gebruiker nooit een product;
- een toekomstige productbinding per gebruiker of expliciete productkeuze kan bovenop dezelfde catalogus worden gebouwd;
- `Visit` bewaart lokale product-id plus externe product-id/location als immutable startcontext;
- `ProviderParkingAction` bewaart de daadwerkelijk gebruikte externe product-id/location;
- continuation, extend/stop, recovery en reconciliation blijven product-scoped;
- `ParkingRuleSet`, `ParkingTariff` en `ParkingBudgetPeriod` zijn product-scoped;
- overlapregels gelden per product, waardoor verschillende producten gelijktijdig verschillende geldige configuratie mogen hebben;
- historische kosten en budgetgebruik filteren per `Visit.ProviderProductId`;
- dashboard/verbruik gebruiken voor de actieve budgetvergelijking het defaultproduct;
- V1-schemawijzigingen worden nog niet als afzonderlijke migration geconsolideerd; een bestaande developmentdatabase moet worden gereset.

## Vervolg

De backendinventarisatie en informatiearchitectuur zijn vastgesteld. De volgende stap is de implementatie opdelen in kleine verticale slices. De eerste slice richt zich op het beheerfundament: de desktop admin-shell, routing/autorisatie en het operationele overzicht/#57, waarbij bestaande Visit-businessflows worden hergebruikt.

## Verticale implementatieslices

De uitvoering van het volledige beheerportaal wordt in deze volgorde opgeknipt.

| Slice | Scope | Relatie |
|---|---|---|
| **8.1 Beheerfundament** | `/beheer` routing, admin-only toegang, desktop shell/navigatie en verhuizing van mobiel beheer naar `/snelbeheer` | basis voor alle volgende slices |
| **8.2 Operationeel dashboard** | actieve Visits, capaciteit, operationele waarschuwingen, Visit openen en bestaande start/stop-use-cases ontsluiten | #57 |
| **8.3 Visit-detail & beheerhistorie** | enriched Visit-detail, ProviderParkingActions, ProviderOperations, filters en beheerhistorie | #61, basis voor diagnose |
| **8.4 Providerstatus & saldo** | officieel 2Park-saldo, freshness/status en actuele provideracties | #58, basis voor reconciliation |
| **8.5 Gebruikers & voertuigen** | desktopbeheer voor gebruikers, voertuigen, toewijzingen en volledige user policies | #95 |
| **8.6 Algemene policies/settings** | default policy, globale capaciteit, long-visit- en budget-warninginstellingen | #95 |
| **8.7 Parkeerregels** | versioned rulesets, betaalvensters, kalenderuitzonderingen, provider-actionduur en continuation | #95 |
| **8.8 Budgetten & tarieven** | budgetperioden, tarieven, lokaal gebruik en kostenberekening | #59, #60 |
| **8.9 Analyse** | aggregatie per gebruiker/kenteken met drill-down naar Visits | #62 |
| **8.10 Providerproducten** | providerproductcatalogus, defaultselectie, productsnapshot en product-scoped configuratie | #95 |
| **8.11 Discrepancies & reconciliation** | persistent discrepancy-model, detectie, historie en herstelcontext | #63 |
| **8.12 Systeem & diagnostiek** | scheduler-, pushdelivery-, health-, audit- en infrastructuurstatus | #95 |

### Slice-richtlijnen

- 8.1 en 8.2 blijven bewust gescheiden: eerst het beheerfundament, daarna de eerste functionele beheerervaring.
- Iedere slice is zo veel mogelijk verticaal: benodigde backend read/write use-cases, API, frontend en tests worden samen afgerond.
- Bestaande businessflows worden hergebruikt; een beheerpagina krijgt geen alternatieve implementatie van bestaande Visit/providerlogica.
- Nieuwe configuratiemutaties krijgen eerst een expliciete application/domain-use-case voordat ze vanuit de frontend worden aangeboden.
- Geen migrations tijdens de V1-ontwikkeling; schemawijzigingen worden volgens de bestaande V1-afspraak later gereconcilieerd.
- CI wordt niet automatisch gecontroleerd; fouten worden onderzocht wanneer een Actions-run wordt aangeleverd.

