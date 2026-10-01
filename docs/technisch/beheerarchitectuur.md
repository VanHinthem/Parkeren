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
| Per-user capaciteit | `UserPolicyOverride.MaxConcurrentVisits` | ja | opnemen in volledige policy-editor |
| Default Visit-policy | `DefaultParkingPolicy` | nee | beheer-use-case en API |
| Overige user policies | modelvelden bestaan | deels | setters, service en API |
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

### Ontbrekende domeinconcepten

Twee onderdelen vragen meer dan alleen ontsluiting van bestaande code:

1. **Parkeerzones** — er bestaat nog geen zelfstandig `ParkingZone`-domeinmodel. De provider levert nu één product/location. Multi-zonebeheer vraagt een expliciet zoneconcept met providerlocatie en geldigheidsperiode.
2. **Persistente discrepancies** — reconciliation bestaat technisch, maar er is nog geen duurzaam discrepancy-record waarmee gedetecteerde en opgeloste afwijkingen voor #63 traceerbaar blijven.

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

- parkeerzones;
- parkeerregels;
- betaalvensters en kalenderuitzonderingen;
- tarieven;
- budgetperioden.

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

## Vervolg

De backendinventarisatie en informatiearchitectuur zijn vastgesteld. De volgende stap is de implementatie opdelen in kleine verticale slices. De eerste slice richt zich op het beheerfundament: de desktop admin-shell, routing/autorisatie en het operationele overzicht/#57, waarbij bestaande Visit-businessflows worden hergebruikt.
