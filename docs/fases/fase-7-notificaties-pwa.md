# Fase 7 — Notificaties en PWA-integratie

**Status: functioneel afgerond; iOS capability-validatie open 🚧**  
**Start: 30 september 2026**

## Doel

Parkeren krijgt een betrouwbare server-side notificatieketen met een in-app inbox. Push, deep links en app-badging worden daarop aangesloten zonder dat parkeerfunctionaliteit afhankelijk wordt van mobiele background execution of OS-delivery.

## Kernbeslissingen

### Notification Inbox is de bron

Iedere relevante gebruikersmelding wordt eerst als `Notification` server-side opgeslagen.

```text
Domeingebeurtenis
    ↓
Notification in database
    ↓
in-app inbox
    ↓
optioneel push delivery
    ↓
optioneel OS/PWA badge
```

Een mislukte of geweigerde push verwijdert de Notification niet en beïnvloedt de onderliggende Visit/provideractie niet.

### Inboxgedrag

V1 ondersteunt:

- lijst van meldingen voor de ingelogde gebruiker;
- gelezen/ongelezen;
- individuele melding als gelezen markeren;
- alles als gelezen markeren;
- individuele melding uit de eigen inbox verwijderen;
- unread count;
- actionable melding openen naar geautoriseerde app-context;
- standaardretentie 90 dagen, configureerbaar.

Verwijderen uit de inbox verwijdert uitsluitend het Notification-record. Visit-, provider-, fout-, reconciliation- en auditgegevens blijven bestaan.

### App-icon badge

Waar het platform dit ondersteunt is de badge gelijk aan het aantal ongelezen server-side inboxmeldingen.

```text
3 ongelezen -> badge 3
1 ongelezen -> badge 1
0 ongelezen -> badge weg
```

De badge is afgeleide UI-state, geen bron van waarheid. Exact gedrag wordt in #78 op echte iOS- en Android-PWA's gevalideerd.

### Push

Push is best effort. Geen parkeerproces, scheduler, continuation, stop, policy of recovery mag afhankelijk zijn van push-permissie of succesvolle aflevering.

## Bouwvolgorde

1. **7.1 Notification foundation/inbox (#80)** — domein, persistence, API, unread/read/delete/retentie en eerste UI.
2. **7.2 Deep links (#77)** — stabiele Visit-routes, auth return-url en server-side autorisatie.
3. **7.3 Visit/scheduler notifications (#76/#53/#55) ✅** — start/stop, succesvolle continuation en mislukte/risicovolle continuation; centrale inbox-delivery met geteste recipientregels voor `StartNewAction` en `ExtendAction`.
4. **7.4 Long Visit warnings (#54)** ✅ — server-side planning/reminders; melding gebruikt een snapshot van bezoeker, kenteken, starttijd en verstreken Visit-duur. Providerstatus en betaalde parkeertijd worden bewust niet in deze waarschuwing getoond.
5. **7.5 Budget warnings (#56) ✅** — configureerbare percentagedrempels (standaard 80/90/100), server-side op gerealiseerde betaalde parkeertijd; iedere drempel maximaal één keer per budgetperiode en alleen voor actieve beheerders.
6. **7.6 Push subscription/delivery ✅** — Web Push bovenop bestaande Notifications met VAPID, browser-subscriptions, duurzame delivery-outbox, retries, verlopen-endpoint cleanup, enable/disable, key-refresh en push deep links.
7. **7.7 Active Visit + badging (#79) ✅** — prominente actieve Visit-status met directe detailnavigatie en app-icon badge op basis van server-side unread count.
8. **7.8 Cross-platform capability spike (#78)** — Android gevalideerd op echte geïnstalleerde PWA; iOS en resterende netwerk/offline edgecases nog open.

## Status per slice

- **7.1 Notification foundation/inbox ✅**
- **7.2 Deep links ✅**
- **7.3 Visit/scheduler notifications ✅**
- **7.4 Long Visit warnings** — afgerond ✅.
- **7.5 Budget warnings** — afgerond ✅; waarschuwend en niet blokkerend, met duurzame threshold-state per budgetperiode.
- **7.6 Push subscription/delivery** — afgerond ✅; push is best effort en losgekoppeld van parkeertransacties, met duurzame delivery-state en maximaal drie vertraagde afleverpogingen.
- **7.7 Active Visit + badging** — afgerond ✅; actieve Visit blijft server-side herleidbaar en prominent zichtbaar in de PWA, met badge als afgeleide unread-state.
- **7.8 Cross-platform capability spike** — Android-validatie grotendeels afgerond ✅; iOS blijft open in #78.

## Bevindingen en validatie 30-09-2026

### Android PWA

Op een echte geïnstalleerde Android/Chrome-PWA zijn succesvol gevalideerd:

- installatie op Home Screen en standalone openen;
- ingelogd blijven na apprestart én volledige telefoonrestart;
- expliciete push-permissie via user interaction;
- push subscription + backendregistratie;
- echte `VisitStarted`- en `VisitStopped`-pushdelivery;
- notification click met open app en vanuit PWA-context;
- startmelding navigeert bewust naar het dashboard;
- stop-/attentionmelding navigeert naar Visit-detail;
- openen van een push markeert de bijbehorende inboxmelding als gelezen en synchroniseert unread count/badge;
- actieve Visit wordt na restart opnieuw server-side opgebouwd;
- `/acties/{VisitId}` werkt als stabiele detailroute;
- service-worker updates laden na deployment automatisch de nieuwe frontend.

Dezelfde matrix moet nog op een actuele iPhone/iOS-PWA worden uitgevoerd voordat #78 en de fase-exit volledig kunnen sluiten.

### Notification navigation

V1 gebruikt doelgerichte navigatie per meldingstype:

- `VisitStarted` -> dashboard;
- `VisitStopped` -> Visit-detail;
- `ProviderContinuationSucceeded` -> dashboard;
- `ProviderContinuationAttentionRequired` -> Visit-detail;
- `LongVisitWarning` -> Visit-detail;
- `BudgetWarning` -> Meldingen.

Wanneer een push wordt geopend, wordt de Notification waar mogelijk direct als gelezen gemarkeerd. Bij cold start wordt de NotificationId tijdelijk in de URL-context meegenomen, waarna de app na authenticatie/readiness dezelfde read-state synchroniseert.

### Recovery/attention

Een Visit die tijdens startup- of periodieke recovery in `AttentionRequired` terechtkomt, creëert nu een idempotente `ProviderContinuationAttentionRequired`-melding. Herhaalde recovery produceert geen dubbele Notification voor dezelfde source-event/context.

### PWA updategedrag

Tijdens Android-validatie is stale app-shell gedrag opgelost:

- `index.html` wordt niet geprecached;
- HTML en `sw.js` worden no-cache/no-store geserveerd;
- service-worker updates worden bij start, foreground/focus/pageshow en periodiek gecontroleerd;
- een nieuwe worker activeert en de app herlaadt één keer na controllerwissel.

Aanvullende versie-info, offline UX, manifestvalidatie, Lighthouse/performancechecks en performancebudget zijn bewust afgesplitst naar #94.

### UX-polish binnen fase 7

De Notification Inbox is mobiel gevalideerd en aangescherpt met:

- correcte enkelvoud/meervoud unreadtekst;
- duidelijk gelezen/ongelezen onderscheid;
- prullenbakactie voor individueel verwijderen;
- contextregel voor attention-, long-visit- en budgetmeldingen;
- functioneel gevalideerde `Alles gelezen`, verwijderen, unread count en lege inbox.

Instellingen toont de actuele pushsubscriptionstatus en kan push op het huidige apparaat in- en uitschakelen.

## Exit

De functionele notificatieketen, inbox, push, deep links, recipient rules, badging en Android-PWA-validatie zijn behaald. **Fase 7 blijft administratief open uitsluitend voor de resterende iOS/cross-platform validatie in #78.** PWA-hardening buiten de capability-spike wordt gevolgd in #94.
