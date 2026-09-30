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

## Exit

Fase 8 is gereed wanneer:

1. de dagelijkse PWA-flows en mobiele Snelbeheer-UX zijn afgerond;
2. de volledige beheer-/administratiefunctionaliteit uit #57–#63 een werkende desktop-first beheerervaring op `/beheer` heeft;
3. de relevante beheerbare backendinstellingen via die beheerervaring kunnen worden onderhouden;
4. historie, analyse en provider/reconciliation-context voldoende zijn voor dagelijks beheer en diagnose.

De dagelijkse PWA-UX is op 30-09-2026 grotendeels afgerond. De resterende hoofdscope van Fase 8 is het volledige beheerportaal en de open dashboard/administratiestories.
