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

## Vervolg

Voor het volledige beheerportaal wordt eerst een aparte inventarisatie gemaakt van alle beheerbare backendinstellingen, acties en diagnostiek. Op basis daarvan worden navigatie, schermen en implementatieslices bepaald.
