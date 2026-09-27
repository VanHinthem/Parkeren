# Parkeerregels en tijdmodel

Parkeerregels worden geëvalueerd in de business-timezone `Europe/Amsterdam`; absolute tijdstippen blijven UTC instants.

Een `ParkingRuleSet` heeft een geldigheidsperiode, configureerbare `PaidWindow`-vensters en `MaxProviderActionDuration`. De rules engine segmenteert een kandidaat-Visit in betaalde en gratis stukken. Gratis tijd telt daardoor niet mee voor `MaxPaidParkingDuration` en vereist later geen 2Park-action.

De huidige ontwikkeltests gebruiken ma–za 09:00–20:00 uitsluitend als testconfiguratie; deze tijden zijn niet als gemeentelijke waarheid in productielogica hardcoded. Feestdagen, handmatige kalenderuitzonderingen, tarieven en versioned persistence volgen in afzonderlijke slices.
