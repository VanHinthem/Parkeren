# PWA capability matrix — fase 7.8

Deze matrix is de uitvoerbasis voor spike #78. Een capability is pas **Validated** nadat deze op een echte geïnstalleerde PWA is getest.

| Capability | Verwachting V1 | Status | Device-validatie |
| --- | --- | --- | --- |
| Installatie/Home Screen + standalone | Supported | Te valideren | iOS + Android |
| Username + 6-cijferige PIN | Supported | Geïmplementeerd | iOS + Android |
| Persistent ingelogd na app/telefoonrestart | Supported with constraints | Te valideren | iOS + Android |
| Meerdere devices + server-side sessie-intrekking | Supported | Te valideren | minimaal 2 clients |
| Web Push bij gesloten/inactieve app | Supported with constraints | Geïmplementeerd | iOS + Android |
| Push-permissie vanuit user interaction | Supported with constraints | Geïmplementeerd | iOS Home Screen + Android |
| Notification deep link naar Visit | Supported | Geïmplementeerd | gesloten + open app |
| Deep-link herstel na authenticatie | Supported | Geïmplementeerd | iOS + Android |
| App-icon badge = unread Notification count | Supported with constraints | Geïmplementeerd | iOS + Android |
| Actieve Visit prominent in app | Supported | Geïmplementeerd | iOS + Android |
| Persistente OS-notificatie actieve Visit | Best effort | Geen V1-afhankelijkheid | onderzoeken |
| Herstel actieve Visit na restart | Supported | Server is bron | iOS + Android |
| Offline/netwerkverlies en herstel | Supported with constraints | Te valideren | iOS + Android |
| Service-worker precache/update/cache | Supported | Geïmplementeerd | iOS + Android |
| Push subscription vervallen/vernieuwen | Supported with constraints | Key refresh + expired cleanup geïmplementeerd | iOS + Android |
| Notification click bij gesloten app | Supported with constraints | Geïmplementeerd | iOS + Android |
| Notification click bij open app | Supported | Geïmplementeerd | iOS + Android |
| Direct reload /acties/{VisitId} | Supported | Geïmplementeerd | productie-PWA |
| HTTPS/service worker | Vereist | Te valideren op deployment | parkeren.vanhinthem.nl |

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
