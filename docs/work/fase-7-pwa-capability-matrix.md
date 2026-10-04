# PWA capability matrix — fase 7.8

Deze matrix is de uitvoerbasis voor spike #78. Een capability is pas **Validated** nadat deze op een echte geïnstalleerde PWA is getest.

| Capability | Verwachting V1 | Status | Device-validatie |
| --- | --- | --- | --- |
| Installatie/Home Screen + standalone | Supported | Android gevalideerd; iOS open | Android: echte geïnstalleerde PWA ✅ |
| Username + 6-cijferige PIN | Supported | Android gevalideerd; iOS open | Android ✅ |
| Persistent ingelogd na app/telefoonrestart | Supported with constraints | Android gevalideerd; iOS open | Android: apprestart + telefoonrestart ✅ |
| Meerdere devices + server-side sessie-intrekking | Supported | Te valideren | minimaal 2 clients |
| Web Push bij gesloten/inactieve app | Supported with constraints | Android gevalideerd; iOS open | Android: echte Visit push ✅ |
| Push-permissie vanuit user interaction | Supported with constraints | Android gevalideerd; iOS open | Android ✅; iOS Home Screen nog testen |
| Notification deep link naar Visit | Supported | Android gevalideerd; iOS open | Android: start -> dashboard, stop/attention -> detail ✅ |
| Deep-link herstel na authenticatie | Supported | Geïmplementeerd; iOS devicevalidatie open | Functioneel getest; iOS nog uitvoeren |
| App-icon badge = unread Notification count | Supported with constraints | Android gevalideerd; iOS open | Android unread/read-sync ✅ |
| Actieve Visit prominent in app | Supported | Android gevalideerd; iOS open | Android ✅ |
| Persistente OS-notificatie actieve Visit | Best effort | Geen V1-afhankelijkheid | onderzoeken |
| Herstel actieve Visit na restart | Supported | Android gevalideerd; iOS open | Android app + telefoonrestart ✅ |
| Offline/netwerkverlies en herstel | Supported with constraints | Te valideren | iOS + Android |
| Service-worker precache/update/cache | Supported | Android gevalideerd; iOS open | Android: consecutive deployments/auto-refresh ✅ |
| Push subscription vervallen/vernieuwen | Supported with constraints | Server-side key update bij herregistratie getest in [PushSubscriptionServiceTests.cs](../../tests/Parkeren.IntegrationTests/Database/PushSubscriptionServiceTests.cs); 404/410-opruiming is geïmplementeerd maar heeft geen gerichte test gevonden. | iOS + Android: echte expiry/refresh nog valideren |
| Notification click bij gesloten app | Supported with constraints | Android gevalideerd; iOS open | Android ✅ |
| Notification click bij open app | Supported | Android gevalideerd; iOS open | Android ✅ |
| Direct reload /acties/{VisitId} | Supported | Android gevalideerd; productie/iOS open | Android dev-PWA ✅ |
| HTTPS/service worker | Vereist | Dev-host gevalideerd; productie nog smoke-testen | Android dev-PWA via HTTPS ✅ |

## Testregels

- Test als **geïnstalleerde PWA**, niet alleen in een browsertab.
- Gebruik minimaal één actuele iPhone/iOS-versie en één actuele Android/Chrome-versie.
- Noteer per rij: device, OS/browser-versie, resultaat en eventuele beperking.
- Push, badge en OS-notificaties zijn nooit bron van waarheid; controleer na afwijkend OS-gedrag altijd dat Inbox en Visit server-side correct blijven.
- Test notification clicks zowel met volledig gesloten PWA als met een reeds geopende PWA.
- Test na apprestart én telefoonrestart dat de actieve Visit opnieuw vanuit de server verschijnt.
- Test service-worker update door een nieuwe clientbuild te deployen en te controleren dat de nieuwe assets actief worden zonder verouderde mutatiecode te blijven gebruiken.

## Exit

#78 kan worden gesloten wanneer alle device-afhankelijke rijen op iOS en Android zijn uitgevoerd en bevindingen als **Supported**, **Supported with constraints**, **Best effort** of **Not supported** zijn vastgelegd. Eventuele functionele gaten worden vóór het sluiten als concrete backlog-items vastgelegd.


## Android-resultaat 30-09-2026

De Android-capabilities zijn getest op een echte geïnstalleerde PWA. Exacte OS-/Chrome-versienummers zijn tijdens deze eerste run niet vastgelegd en moeten bij een volgende matrixrun wel worden genoteerd.

Extra bevindingen:

- openen van een push markeert de bijbehorende Notification als gelezen;
- unread count en badge volgen daarna direct de server-side inboxstate;
- pushrouting is per type bewust verschillend: start/succes naar dashboard, stop/attention/long-visit naar detail;
- service-worker/cacheproblemen met een stale app-shell zijn tijdens de test opgelost;
- aanvullende offline UX en build/versiondiagnostiek zijn afgesplitst naar #94.

Nog open voor #78:

- volledige iOS/iPhone-matrix;
- expliciete offline/netwerkverlies-hersteltest;
- meerdere devices + server-side sessie-intrekking als capabilitytest;
- push subscription expiry/refresh als devicevalidatie;
- productie-host smoke test.
