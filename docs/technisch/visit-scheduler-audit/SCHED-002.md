# SCHED-002 — gratis periode / overnight naar volgende betaalde periode

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Scenario:** een Visit loopt over een gratis periode heen en heeft daarna opnieuw betaalde providerdekking nodig.

## Gewenste invariant

Tijdens gratis tijd mag geen providerdekking nodig zijn. Vóór het volgende betaalde segment moet wel tijdig en betrouwbaar een provideraction gereedstaan, zonder afhankelijkheid van toevallige worker- of providerlatency op de grens.

## As-built gedrag

De rules engine segmenteert de Visit correct in betaalde en gratis perioden. De eerste provideraction wordt begrensd op het einde van het betaalde venster. Als later opnieuw betaald parkeren nodig is, wordt `ContinueProviderCoverage` op `nextPaid.Start` gezet.

Wanneer het work op dat moment wordt verwerkt:

1. wordt de eerdere lokale action nog als actuele `Active` action gevonden;
2. wordt de provider opnieuw uitgelezen;
3. de eerdere action moet remote nog `active` zijn en dezelfde eindtijd hebben;
4. daarna wordt de eerdere action lokaal `Completed` gemaakt;
5. `ProcessInitialCoverageAsync` start de volgende betaalde dekking.

## Bevinding

De scheduler wordt pas **op het begin van het volgende betaalde segment** wakker. Daarmee begint de provider-call pas wanneer betaalde dekking al nodig is. Database-claimtijd, worker-loop en providerlatency kunnen daardoor een gat veroorzaken.

De live 2Park-test uit #71 heeft al bewezen dat een action vijf minuten in de toekomst als `scheduled` kan worden aangemaakt. Het huidige overnight-pad benut die mogelijkheid niet.

Daarnaast veronderstelt de processor bij hervatting dat de vorige remote action na zijn eindtijd nog als `active` terugkomt. Het echte 2Park-gedrag na natuurlijke expiratie is nog niet vastgelegd. De mock is hiervoor geen bewijs; zie [SCHED-016](SCHED-016.md).

## Bestaande testdekking

Er zijn domain-tests voor overnight-segmentatie en integratietests die controleren dat work op het volgende betaalde beginmoment wordt gepland. Dat bewijst de lokale planning, maar niet een realistische provider-state transition over de nacht.

## Relaties

- [SCHED-001](SCHED-001.md): dezelfde vraag wanneer een toekomstige successor moet worden aangemaakt.
- [SCHED-013](SCHED-013.md): natuurlijke afronding van actions/Visits.
- [SCHED-016](SCHED-016.md): de mock veroudert action-statussen niet.
- [SCHED-017](SCHED-017.md): timestampvergelijking met live providerdata.

## Onduidelijkheden / open vragen

1. Hoe exposeert 2Park een action nadat `TIMEEND` natuurlijk is verstreken: ontbrekend, `completed`, `stopped` of nog tijdelijk `active`?
2. Hoe lang blijft zo'n action via read-back zichtbaar?
3. Welk precheckmoment willen we voor een nieuw betaald segment na een lange gratis periode: bijvoorbeeld T-5 vóór `nextPaid.Start`?

## Voorlopige conclusie

De segmentatie is functioneel goed, maar de hervatting is qua timing en remote-state-aanname nog niet betrouwbaar bewezen. Dit scenario moet samen met SCHED-001 worden ontworpen, niet als losse fix.