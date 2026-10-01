# Fase 8 — Dashboards, historie en administratie

**Status: gestart 🚧**  
**Start: 30 september 2026**

## Doel

Fase 8 rondt de dagelijkse PWA-ervaring af en bouwt de beheer-/administratielaag verder uit. Tijdens de mobiele UX-validatie is besloten de dagelijkse PWA en het volledige backendbeheer expliciet te scheiden.

## Kernbesluit: PWA versus volledig beheer

Parkeren krijgt twee beheerervaringen met ieder een eigen doel.

### PWA — dagelijks gebruik + Snelbeheer

De PWA blijft primair gericht op parkeren uitvoeren:

- dashboard;
- actieve Visit;
- starten, stoppen en waar toegestaan verlengen;
- eigen historie en Visit-detail;
- meldingen;
- toegewezen auto's;
- persoonlijke instellingen;
- een beperkt mobiel **Snelbeheer** voor beheerders.

Het bestaande mobiele beheer blijft functioneel beperkt tot snelle operationele handelingen:

- bezoeker aanmaken;
- voertuig aanmaken;
- voertuig koppelen/ontkoppelen;
- gebruiker activeren/deactiveren;
- voertuig activeren/deactiveren;
- PIN resetten;
- gebruikerssessies intrekken.

Deze mobiele beheerpagina groeit niet mee met alle backendinstellingen.

Doelroute zodra het volledige beheerportaal wordt geïntroduceerd:

`/snelbeheer`

De mobiele bottom-navigation mag voor de gebruiker gewoon **Beheer** blijven heten.

### Volledig beheerportaal

Het volledige beheer komt op:

`parkeren.vanhinthem.nl/beheer`

Dit wordt een aparte desktop-first beheerervaring binnen dezelfde applicatiearchitectuur:

- dezelfde backend;
- dezelfde database;
- dezelfde domeinlogica;
- dezelfde authenticatie/autorisatie;
- geen duplicatie van businesslogica;
- wel een eigen beheergerichte frontend-shell en informatiearchitectuur.

De verdere scope en inventarisatie worden bijgehouden in #95 en `docs/technisch/beheerarchitectuur.md`.

## Stand van de PWA-UX

### 8.1 Bezoekersdashboard (#81) — implementatie afgerond ✅

Het dashboard is op echte Android-PWA gevalideerd en bevat:

- prominente actieve Visit;
- capaciteit;
- starten van een Visit;
- directe Stoppen/Verlengen-acties;
- recente eigen Visits;
- navigatie naar historie/detail;
- uitsluitend Visit-niveau voor de bezoeker, zonder financiële/providerdetails.

UX-besluit: de actieve Visit-card zelf is bewust geen navigatiekaart. De primaire acties staan direct op de kaart/eronder; historie- en notificatieroutes openen Visit-detail wanneer detailcontext nodig is.

Cross-platform devicevalidatie blijft centraal gevolgd in #78; dit blokkeert de implementatiestatus van #81 niet.

### 8.2 Parkeeracties/historie — dagelijkse PWA-flow afgerond ✅

De mobiele historie is aangescherpt met:

- compacte, klikbare Visit-rows;
- herbruikbare Nederlandse kentekenpresentatie;
- duidelijke datum/tijd en duur;
- `<1 min` voor zeer korte Visits;
- status-/kleuronderscheid;
- Visit-detail met Nederlandse statuslabels;
- gestart/gestopt/duur/gepland-tot als kernfeiten;
- context-aware terugnavigatie:
  - Dashboard -> detail -> Dashboard;
  - Acties -> detail -> Acties;
  - Meldingen -> detail -> Meldingen;
  - directe/push-link -> veilige fallback naar Acties.

Dit is de gebruikershistorie. De bredere beheerdershistorie uit #61 — filters, provideractions, toegepaste regels/tarieven, externe acties en auditdiagnose — blijft open voor het volledige beheerportaal.

### 8.3 Auto's — afgerond ✅

De pagina Auto's toont alleen voertuigen die voor de ingelogde gebruiker zijn toegestaan en gebruikt dezelfde compacte NL-kentekencomponent als Dashboard/Historie/Snelbeheer.

De pagina is bewust eenvoudig gehouden; voor normaal gebruik worden maar weinig voertuigen verwacht.

### 8.4 Instellingen — afgerond ✅

Instellingen bevat:

- account/rol;
- uitloggen;
- actuele pushsubscriptionstatus;
- push inschakelen/uitschakelen;
- PIN wijzigen.

De layout is mobiel compacter gemaakt. Build/version-info, offline UX en aanvullende PWA-hardening zijn bewust afgesplitst naar #94.

### 8.5 Meldingen — afgerond ✅

De Notification Inbox is functioneel en visueel gevalideerd:

- gelezen/ongelezen;
- unread count;
- alles gelezen;
- individueel verwijderen;
- lege inbox;
- notification click/deep link;
- read-state en badge synchroniseren;
- relevante contextregels per meldingstype.

De notificatiearchitectuur en platformvalidatie blijven gedocumenteerd onder Fase 7.

### 8.6 Snelbeheer — functionele scope bevroren, mobiele UX afgerond ✅

De bestaande beheerpagina blijft bestaan als snelle mobiele beheertool. Functionaliteit wordt niet verder uitgebreid in de PWA.

UX-aanpassingen:

- compactere formulieren;
- compactere gebruikers-/voertuigrijen;
- NL-kentekencomponent;
- subtiele actief/inactief-status;
- compact koppelen van voertuigen;
- compact Accountbeheer;
- PIN-fields als password-input;
- standaardselectie van een Visitor, met eerste gebruiker als fallback;
- acties die context vereisen zijn disabled zolang geen geldige selectie bestaat.

## Open beheer-/administratiescope

De volgende stories blijven open en worden in beginsel onderdeel van het volledige beheerportaal #95:

- #57 Actieve bezoeken bekijken / operationeel beheer-dashboard;
- #58 Officieel 2Park-saldo bekijken;
- #59 Jaarverbruik bekijken;
- #60 Parkeerkosten bekijken;
- #61 Beheerdershistorie/filtering/provider-audit;
- #62 Verbruik per bezoeker/kenteken analyseren;
- #63 Afwijkingen met 2Park signaleren.

Waar deze stories een eenvoudige PWA-weergave overlappen, geldt de mobiele implementatie als dagelijkse gebruikersflow; uitgebreide analyse, filtering, providercontext, configuratie en herstel horen bij `/beheer`.

## Beheerbare configuratie

Een uitgangspunt voor het volledige beheerportaal is dat backendvariabelen niet als verborgen appsettings blijven bestaan wanneer ze functioneel beheerbaar horen te zijn. De inventarisatie voor #95 omvat onder meer:

- parkeerregels en betaalvensters;
- parkeerzones + geldigheidsperioden;
- provider-/systeemlimieten;
- algemene en per-user policies;
- maximale Visit- en sessieduur;
- Visit extension/open-ended beleid;
- budgetten en waarschuwingsdrempels;
- notificatie-instellingen;
- actieve Visits/provideracties;
- discrepancies/reconciliation;
- audit/providerhistorie;
- systeemstatus/diagnostiek.

## Vastgestelde beheerarchitectuur — 1 oktober 2026

De eerste backendinventarisatie voor #95 is afgerond.

Belangrijkste conclusie: veel benodigde domeinlogica bestaat al. Default/user policies, versioned parkeerregels, betaalvensters, kalenderuitzonderingen, provider-sessieduur, continuation, tarieven, budgetberekening, Visit/provider lifecycle en recovery zijn reeds als domeinconcept aanwezig. De belangrijkste resterende werkzaamheden zijn beheergerichte application services/API's, read models en de desktop frontend.

Het `ParkingZone`-domein is in 8.10 toegevoegd. Het resterende nieuwe domeinconcept binnen Fase 8 is met name:

- persistente discrepancies voor traceerbare reconciliation.

Het beheerportaal krijgt zes hoofdgebieden:

1. **Overzicht** — operationele status, actieve Visits, capaciteit, saldo en waarschuwingen;
2. **Bezoeken** — actief, historie, detail, verbruik en kosten;
3. **Gebruikers & voertuigen** — accounts, voertuigen, toewijzingen en user policies;
4. **Parkeerconfiguratie** — zones, rulesets, betaalvensters, uitzonderingen, tarieven en budgetperioden;
5. **Provider & reconciliatie** — providerstatus/actions/operations, discrepancies en recoverycontext;
6. **Systeem** — algemene policies/settings, notificaties, diagnostiek, scheduler/push en audit.

Historische/providerdata is in beginsel read-only. Mutaties lopen via expliciete application/domain-use-cases; `/beheer` wordt geen generieke database-editor.

Functionele configuratie wordt beheerbaar, maar secrets zoals database-/2Park-credentials en VAPID private keys blijven environment/secrets en worden niet via het portaal wijzigbaar.

Zie voor de volledige inventarisatie en pagina-indeling `docs/technisch/beheerarchitectuur.md`.

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
| **8.10 Zones** | nieuw ParkingZone-domein en multi-zonebeheer | #95 |
| **8.11 Discrepancies & reconciliation** | persistent discrepancy-model, detectie, historie en herstelcontext | #63 |
| **8.12 Systeem & diagnostiek** | scheduler-, pushdelivery-, health-, audit- en infrastructuurstatus | #95 |

### Slice-richtlijnen

- 8.1 en 8.2 blijven bewust gescheiden: eerst het beheerfundament, daarna de eerste functionele beheerervaring.
- Iedere slice is zo veel mogelijk verticaal: benodigde backend read/write use-cases, API, frontend en tests worden samen afgerond.
- Bestaande businessflows worden hergebruikt; een beheerpagina krijgt geen alternatieve implementatie van bestaande Visit/providerlogica.
- Nieuwe configuratiemutaties krijgen eerst een expliciete application/domain-use-case voordat ze vanuit de frontend worden aangeboden.
- Geen migrations tijdens de V1-ontwikkeling; schemawijzigingen worden volgens de bestaande V1-afspraak later gereconcilieerd.
- CI wordt niet automatisch gecontroleerd; fouten worden onderzocht wanneer een Actions-run wordt aangeleverd.

## Voortgang beheerportaal — 1 oktober 2026

De beheerimplementatie staat inmiddels op **slice 8.10 in CI-validatie**:

- 8.1 beheerfundament ✅
- 8.2 operationeel dashboard ✅
- 8.3 Visit-detail & beheerhistorie ✅
- 8.4 providerstatus & officieel saldo ✅
- 8.5 gebruikers & voertuigen ✅
- 8.6 algemene policies/settings ✅
- 8.7 parkeerregels ✅
- 8.8 budgetten & tarieven ✅
- 8.9 analyse ✅
- 8.10 zones 🚧 implementatie gereed, CI-validatie volgt

### 8.5 Gebruikers & voertuigen

Beschikbaar in het desktop beheerportaal:

- `/beheer/gebruikers` — gebruikerslijst, bezoeker aanmaken, activeren/deactiveren;
- `/beheer/gebruikers/{id}` — detail, toegewezen voertuigen, PIN reset, sessies intrekken en user policy overrides;
- `/beheer/voertuigen` — voertuigenlijst, voertuig aanmaken en activeren/deactiveren.

De policy-editor ondersteunt de bestaande velden:

- `MaxPaidParkingDuration`;
- `MaxVisitElapsedDuration`;
- `AllowVisitExtension`;
- `AllowOpenEndedVisits`;
- `MaxConcurrentVisits`.

Per veld is zichtbaar of de waarde uit de default komt of een user override is. Terugzetten op **Standaard** verwijdert die override. De effectieve waarde wordt apart getoond.

Server-side beveiliging:

- policywijzigingen die de EffectiveParkingPolicy veranderen worden geblokkeerd zolang de gebruiker een actieve Visit heeft;
- `MaxConcurrentVisits` kan niet boven de globale capaciteit worden gezet;
- gebruiker of voertuig deactiveren wordt geblokkeerd wanneer er een relevante actieve Visit bestaat;
- bestaande StartVisit-/policy enforcement blijft leidend.

De duur-overrides zijn aangescherpt naar drie expliciete toestanden: **Standaard**, **Limiet** en **Onbeperkt**. Dit wordt persistent vastgelegd via `PolicyDurationOverrideMode`, zodat een begrensde default per gebruiker expliciet onbeperkt kan worden gemaakt zonder ambigu `null`-gedrag.

### 8.6 Algemene policies/settings

Beschikbaar op `/beheer/systeem`:

- standaard user policy beheren;
- maximale betaalde parkeertijd begrensd of onbeperkt;
- maximale totale Visitduur begrensd of onbeperkt;
- `AllowVisitExtension`;
- `AllowOpenEndedVisits`;
- default `MaxConcurrentVisits`;
- globale capaciteit;
- Long Visit-warningtijd;
- adminnotificatie bij Long Visit;
- Long Visit-reminderinterval;
- budgetwaarschuwingsdrempels.

Default-policywijzigingen worden server-side **per veld** getoetst tegen actieve Visits. Alleen actieve gebruikers die het gewijzigde veld van de default erven blokkeren de wijziging. De UI toont bij een conflict het aantal geraakte actieve Visits en de betreffende velden.

De globale capaciteit behoudt de strengere bestaande lock: wijzigen kan niet zolang er actieve Visits zijn. Bij verlagen worden hogere user-overrides én de default concurrency automatisch naar het nieuwe globale maximum geklemd.

Long Visit scheduler-semantiek: bestaande reeds geplande warning-work behoudt zijn huidige `DueAt`. Nieuwe Visits gebruiken de nieuwe warning threshold; wanneer een warning wordt verwerkt gebruikt de worker de actuele admin-notificatie- en reminderinstellingen.

Generieke persistente beheer-audit volgt in 8.12; daarom blijft #74 nog open ondanks dat de functionele default/user policy-beheercriteria zijn gerealiseerd.

### 8.7 Parkeerregels

De beheerroute `/beheer/configuratie/parkeerregels` beheert parkeerregels als tijdsgebonden versies.

Ondersteund:

- alle rulesetversies read-only terugzien;
- nieuwe toekomstige versie plannen;
- betaalvensters per weekdag, inclusief meerdere niet-overlappende vensters per dag;
- expliciete kalenderuitzonderingen per datum als betaald/gratis;
- Nederlandse feestdagen gratis aan/uit;
- `MaxProviderActionDuration`;
- `Continuation = StartNewAction | ExtendAction`.

Versioningsemantiek:

- historische records worden niet aangepast;
- een nieuwe versie sluit de vorige open-ended versie automatisch op dezelfde `ValidFrom`;
- `ValidFrom` moet in de toekomst liggen;
- overlap tussen rulesetversies wordt voorkomen;
- actieve Visits mogen een toekomstige rulesetboundary passeren; de bestaande period-segmentatie gebruikt per tijdvak de geldige ruleset.

Domeinvalidatie is toegevoegd voor overlappende betaalvensters en dubbele kalenderuitzonderingen. Domain- en PostgreSQL-integratietests dekken validatie, future-only versioning en het atomisch afsluiten van de vorige versie.

Er is geen databaseschemawijziging nodig.

### 8.8 Budgetten & tarieven

Configuratie:

- `/beheer/configuratie/budgetten` — append-only budgetperioden met `ValidFrom`, `ValidUntil` en maximale betaalde duur;
- budgetperioden mogen niet overlappen;
- invoerdefault voor Oss is 1500 uur per kalenderjaar, maar de waarde is beheerbaar en niet hardcoded in enforcement;
- `/beheer/configuratie/tarieven` — historische/versioned uurtarieven;
- bounded historische tarieven kunnen worden backfilled;
- een nieuwe open-ended tariefversie sluit automatisch de vorige open-ended versie op dezelfde `ValidFrom`;
- tarieven mogen niet overlappen.

Rapportage:

- `/beheer/verbruik` toont lokaal gebruikt/resterend/totaal per gekozen budgetperiode;
- het beheer-dashboard toont het actuele lokale budget naast het officiële 2Park-saldo;
- lokaal budgetgebruik telt uitsluitend betaalde tijd van afgeronde Visits;
- historische budgetperioden kunnen afzonderlijk worden bekeken;
- het officiële providersaldo wordt met eigen unit/freshness getoond en blijft een aparte providerbron;
- alleen wanneer de provider een tijdseenheid (`Minute`) retourneert wordt een lokale/provider-discrepantie in tijd berekend; een euro- of keer-saldo wordt niet kunstmatig naar uren vertaald.

Kostenrapportage:

- beheerder kiest een periode;
- alleen afgeronde Visits worden als gerealiseerde kosten meegenomen;
- gratis tijd kost €0;
- betaalde segmenten worden eerst langs rulesetgrenzen en vervolgens langs tariefgrenzen gesplitst;
- iedere segmentkost gebruikt de tariefversie die op dat moment geldig was;
- totals en betaalde duur worden getoond met drill-down naar Visit-detail;
- ontbrekende historische rules/tarieven leveren expliciet **onvolledige** rijen/totalen op; er vindt geen fallback naar huidig tarief of stilzwijgende €0-herberekening plaats.

Er is geen databaseschemawijziging nodig; bestaande `ParkingBudgetPeriod` en `ParkingTariff` persistence wordt gebruikt. Domain- en integratietests dekken overlap, tariefversioning, betaalde budgettijd, tariefgrensberekening en ontbrekende historische tarieven.

### 8.9 Analyse

Beschikbaar op `/beheer/analyse`:

- gekozen periode analyseren;
- aggregatie per bezoeker;
- aggregatie per kenteken;
- minimaal aantal Visits, betaalde parkeerduur en berekende kosten;
- gratis tijd telt niet mee als betaalde duur;
- dezelfde historische ruleset-/tariefberekening als 8.8 wordt hergebruikt;
- onvolledige historische configuratie blijft expliciet zichtbaar als onvolledig;
- gedeelde kentekens worden per Visit aan `Visit.UserId` gekoppeld, zodat gebruik per bezoeker gescheiden blijft;
- per kenteken blijven de onderliggende bezoekernamen zichtbaar;
- inactieve gebruikers en voertuigen worden als **Gearchiveerd** gemarkeerd in historische analyses;
- elke aggregatie kan worden uitgeklapt naar de onderliggende Visits;
- vanuit iedere Visitregel kan direct naar het bestaande Visit-detail worden genavigeerd.

De analyse-readmodels introduceren geen nieuwe persistence en geen schemawijziging. Een PostgreSQL-integratietest dekt expliciet een gedeeld kenteken over twee bezoekers en gearchiveerde gebruiker/voertuig-status.

### 8.10 Zones

Beschikbaar op `/beheer/configuratie/zones`:

- nieuw persistent `ParkingZone`-domein met naam, 2Park `ProviderLocation`, `ValidFrom`, optionele `ValidUntil` en `IsDefault`;
- meerdere niet-default zones mogen tegelijk geldig zijn;
- voor nieuwe Visits moet op het startmoment precies één geldige defaultzone bestaan;
- de eerste defaultzone mag historisch worden backfilled;
- wanneer al een defaultzone bestaat, moet een vervangende default in de toekomst ingaan;
- een nieuwe toekomstige open default sluit de bestaande open default automatisch op dezelfde `ValidFrom`;
- open zones kunnen expliciet op een toekomstige datum worden afgesloten;
- zonebeheer blijft beschikbaar wanneer de provider tijdelijk niet bereikbaar is; providerstatus wordt alleen best-effort gebruikt om het eerste formulier voor te vullen.

Operationele integratie:

- `Visit.ParkingZoneId` legt de gekozen zone vast bij StartVisit;
- de normale API-start retourneert 503 zolang geen geldige defaultzone is geconfigureerd;
- de 2Park `LOCATION` voor de eerste provideractie komt uit `ParkingZone.ProviderLocation`;
- continuation-actions en replacement-actions gebruiken dezelfde zone als de oorspronkelijke Visit;
- een actieve Visit wisselt daardoor niet van zone wanneer later een andere defaultzone wordt gepland;
- legacy Visits met `ParkingZoneId = null` blijven leesbaar en kunnen, waar nodig, nog terugvallen op de providerproduct-location;
- Visit-detail toont de vastgelegde zone en provider-location.

Deze slice introduceert wél een schema-uitbreiding: tabel `parking_zones` plus nullable `ParkingZoneId` op `visits`. Conform de V1-afspraak is de bestaande Initial-baseline bijgewerkt in plaats van een nieuwe migration toe te voegen. **Na groene CI moet de developmentdatabase opnieuw worden aangemaakt** voordat zones functioneel worden getest.

Domain-, application- en PostgreSQL-integratietests dekken default-resolutie, overlap, zone-snapshot op Visit, toekomstige defaultversioning en de StartVisit-resolver.

Na groene CI is de volgende slice: **8.11 Discrepancies & reconciliation**.

## Exit

Fase 8 is gereed wanneer:

1. de dagelijkse PWA-flows en mobiele Snelbeheer-UX zijn afgerond;
2. de volledige beheer-/administratiefunctionaliteit uit #57–#63 een werkende desktop-first beheerervaring op `/beheer` heeft;
3. de relevante beheerbare backendinstellingen via die beheerervaring kunnen worden onderhouden;
4. historie, analyse en provider/reconciliation-context voldoende zijn voor dagelijks beheer en diagnose.

De dagelijkse PWA-UX is op 30-09-2026 grotendeels afgerond. De resterende hoofdscope van Fase 8 is het volledige beheerportaal en de open dashboard/administratiestories.
