# Parkeerregels en tijdmodel

Parkeerregels worden geëvalueerd in de business-timezone `Europe/Amsterdam`; absolute tijdstippen blijven UTC instants.

Een `ParkingRuleSet` heeft een geldigheidsperiode, configureerbare `PaidWindow`-vensters en `MaxProviderActionDuration`. De rules engine segmenteert een kandidaat-Visit in betaalde en gratis stukken. Gratis tijd telt daardoor niet mee voor `MaxPaidParkingDuration` en vereist later geen 2Park-action.

De regel bevat ook `Continuation`: `StartNewAction` (standaard voor Oss) of `ExtendAction`. Voor Oss duurt één 2Park-parkeeractie maximaal vier uur. Daarna kan hetzelfde kenteken opnieuw worden aangemeld als de Visit doorloopt. De scheduler verwerkt deze keuze in een volgende fase-6-stap; het vastleggen van de regel verandert de huidige providerflow nog niet.

De huidige ontwikkeltests gebruiken ma–za 09:00–20:00 uitsluitend als testconfiguratie; deze tijden zijn niet als gemeentelijke waarheid in productielogica hardcoded. Feestdagen, handmatige kalenderuitzonderingen, tarieven en versioned persistence volgen in afzonderlijke slices.
