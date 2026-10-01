# Frontend designarchitectuur

De visuele laag van Parkeren is bewust gescheiden van functionele en domeinlogica. Zie ADR #91.

## Richting

De frontend wordt opgebouwd uit:

```text
app/          routing en composition
features/     functionele schermen
api/          backendcommunicatie
components/   herbruikbare domein-UI
design/       visuele foundation
```

Binnen `design/` komen tokens, primitives, layouts, iconen en globale styles.

Features mogen het design system gebruiken. Het design system mag geen features, API-client of parkeerlogica kennen.

## Design tokens

Kleuren, spacing, typography, radius en elevation worden gecentraliseerd en semantisch benoemd. Hierdoor kan de uitstraling worden aangepast zonder verspreide wijzigingen in functionele componenten.

## Componenten

Algemene elementen zoals buttons, inputs, cards, dialogs, alerts en badges worden via eigen dunne UI-primitives aangeboden. Een externe UI-library kan later achter deze grens worden gebruikt zonder featurecode daar onnodig aan te koppelen.

## Responsive en toegankelijk

De PWA wordt mobile-first ontworpen en blijft bruikbaar voor beheer op grotere schermen. Interactie is touchvriendelijk en gebruikt semantische HTML, keyboard/focusondersteuning en toegankelijke statuscommunicatie.

## Visueel ontwerp

De definitieve huisstijl is nog niet vastgesteld. Kleurpalet, typografie, navigatie, iconografie en concrete schermlayouts worden vóór brede UI-implementatie als aparte designstap uitgewerkt en hier gedocumenteerd.


## Gekozen visuele richting

De V1 gebruikt de Modern & Clean-layout met één gedeelde componentstructuur en twee themes: light en dark.

Themevoorkeur heeft drie standen: `system` (standaard), `light` en `dark`. `system` volgt `prefers-color-scheme`; een expliciete light/dark-keuze overschrijft dit. De voorkeur wordt lokaal persistent opgeslagen en staat volledig los van parkeer-/domeinlogica.

Beide themes worden uitsluitend via semantische design tokens/CSS custom properties opgebouwd. Featurecomponenten bevatten geen aparte light/dark business- of layoutlogica. Bij initialisatie wordt het theme zo vroeg mogelijk toegepast om een verkeerde kleurflits te voorkomen.
