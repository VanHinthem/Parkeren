# Fase 7 — Notificaties en PWA-integratie

**Status: gestart 🚧**  
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
4. **7.4 Long Visit warnings (#54)** — server-side planning/reminders.
5. **7.5 Budget warnings (#56)**.
6. **7.6 Push subscription/delivery** — bovenop bestaande Notifications.
7. **7.7 Active Visit + badging (#79)**.
8. **7.8 Cross-platform capability spike (#78)** — echte iOS/Android validatie en backlogcorrecties.

## Status per slice

- **7.1 Notification foundation/inbox ✅**
- **7.2 Deep links ✅**
- **7.3 Visit/scheduler notifications ✅**
- **7.4 Long Visit warnings** — volgende slice.

## Exit

Fase 7 is gereed wanneer de functionele notificatieketen server-side en in-app werkt, push/deep links/badging zijn aangesloten, relevante recipient rules zijn getest en de PWA-capabilities op iOS en Android daadwerkelijk zijn gevalideerd.
