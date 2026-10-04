# Fase 8 — Afronding

**Status: afgerond ✅**  
**Afgerond: 2 oktober 2026**

## Resultaat

Fase 8 levert twee duidelijk gescheiden beheerervaringen:

- de dagelijkse PWA voor parkeren uitvoeren, eigen historie, meldingen, voertuigen, instellingen en mobiel Snelbeheer;
- het volledige desktop-first beheerportaal op `/beheer` voor operationeel beheer, configuratie, analyse, provider/reconciliation, diagnostiek en audit.

De implementatie hergebruikt dezelfde backend, database, domeinlogica, authenticatie en autorisatie. `/beheer` is geen generieke database-editor; mutaties lopen via expliciete application/domain-use-cases.

## Beheerportaal — afgeronde slices

- **8.1 Beheerfundament** ✅ — admin-only desktop-shell op `/beheer`; mobiel beheer als `/snelbeheer`.
- **8.2 Operationeel dashboard** ✅ — actieve Visits, capaciteit, aandachtstatus, starten namens gebruiker en stoppen via bestaande Visit-flows (#57).
- **8.3 Visit-detail & beheerhistorie** ✅ — filters, Visit-detail, ProviderParkingActions/ProviderOperations en relevante historische context (#61).
- **8.4 Providerstatus & saldo** ✅ — officieel 2Park-saldo, freshness/stale-semantiek en actuele provideractions (#58).
- **8.5 Gebruikers & voertuigen** ✅ — accounts, voertuigen, toewijzingen, PIN/sessies en per-user policy overrides.
- **8.6 Algemene policies/settings** ✅ — default policy, globale capaciteit en warninginstellingen.
- **8.7 Parkeerregels** ✅ — versioned rulesets, betaalvensters, kalenderuitzonderingen, provider-actionduur en continuation.
- **8.8 Budgetten & tarieven** ✅ — budgetperioden, tarieven, lokaal verbruik en historische kosten (#59/#60).
- **8.9 Analyse** ✅ — aggregatie per bezoeker/kenteken met Visit-drill-down (#62).
- **8.10 Providerproducten** ✅ — lokale 2Park-productcatalogus, defaultproduct, product-scoped configuratie en Visit/action-productsnapshot.
- **8.11 Discrepancies & reconciliation** ✅ — persistente provider/local-afwijkingen, historie, detectie en herstelcontext (#63).
- **8.12 Systeem & diagnostiek** ✅ — veilige systeemdiagnostiek plus generieke persistente beheer-audit.

## 8.12 — eindstatus

### Diagnostiek

`/beheer/systeem/diagnostiek` toont read-only:

- database-health;
- veilige provider- en Web Push-configuratiestatus zonder secrets;
- scheduler pending/claimed/overdue en relevante timestamps;
- pushdelivery pending/failed en relevante timestamps.

### Generieke beheer-audit

Muterende beheerflows schrijven persistente `AdminAuditEvent`-records met:

- actor;
- action;
- target type/id;
- timestamp;
- expliciet samengestelde veilige context.

PINs, hashes, sessietokens, providercredentials, VAPID private keys en andere secrets worden niet opgenomen. Gespecialiseerde Visit/providerhistorie blijft een afzonderlijke bron en wordt niet gedupliceerd als technisch log.

### Audit read/API/UI

Beschikbaar:

- `GET /api/admin/system/audit`;
- admin-only toegang;
- actor username;
- nieuwste eerst;
- begrensd resultaat (maximaal 200 server-side; UI toont standaard maximaal 100);
- filters op actor, actie, targettype, target-id en periode;
- `/beheer/systeem/audit` met actor, actie, target, tijdstip en veilige context.

## Afgeronde dashboard-/administratiestories

De Fase-8 stories #57 t/m #63 zijn afgerond. #74 is eveneens gesloten nadat default/user policywijzigingen generiek traceerbaar zijn gemaakt via de beheer-audit.

## Buiten Fase 8

De volgende punten blijven bewust buiten deze fase:

- iOS/cross-platform PWA capability-validatie (#78);
- aanvullende PWA hardening, build/version-info, offline UX en performancechecks (#94);
- import van bestaande 2Park-historie en koppeling aan het Oss-budget (#93);
- resterende echte 2Park capacity/JIT/error-hardening (#71 / Fase 9);
- definitieve V1 migration-consolidatie en release/deploymentwerk.

Deze open punten veranderen de functionele afronding van Fase 8 niet.

## Exitcriteria

De Fase-8 exitcriteria zijn behaald:

1. dagelijkse PWA-flows en mobiel Snelbeheer zijn afgerond;
2. de beheer-/administratiescope van #57–#63 is beschikbaar in het desktop-first beheerportaal;
3. relevante functionele backendinstellingen kunnen via expliciete beheer-use-cases worden onderhouden;
4. historie, analyse, provider/reconciliation, diagnostiek en generieke beheer-audit bieden voldoende context voor dagelijks beheer en diagnose.

Zie voor detailhistorie en ontwerpbesluiten:

- [Fase 8 — dashboards en administratie](fase-8-dashboards-administratie.md);
- `docs/technisch/beheerarchitectuur.md`;
- issue #95.
