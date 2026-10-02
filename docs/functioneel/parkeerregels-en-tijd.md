# Parkeerregels en tijdmodel

Parkeerregels worden geëvalueerd in de business-timezone `Europe/Amsterdam`; absolute persistente tijdstippen blijven UTC-instants.

Een versioned `ParkingRuleSet` beschrijft per providerproduct:

- geldigheidsperiode;
- betaalde weekdag/tijdvensters (`PaidWindows`);
- kalenderuitzonderingen;
- feestdaggedrag;
- `MaxProviderActionDuration`;
- continuationstrategie (`StartNewAction` of `ExtendAction`).

De rules engine segmenteert een Visit in betaalde en gratis stukken. Gratis tijd:

- telt niet mee voor `MaxPaidParkingDuration`;
- vereist geen provideraction;
- beëindigt de logische Visit niet automatisch.

## Oss

Voor Oss is V1 momenteel:

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
```

De betaalde vensters worden uit de persistente rulesets gelezen en niet als providerwaarheid in schedulerlogica hardcoded.

## Continuation over een providergrens

Bij aaneengesloten betaalde providerdekking controleert de scheduler vijf minuten vóór het geplande action-einde of een successor nodig is. Door de bevestigde overlapcontrole van 2Park start een aansluitende successor op:

```text
predecessor.End + 1 seconde
```

Bij een echte gratis periode geldt een andere invariant:

```text
geen providerdekking tijdens gratis tijd
successor.Start = nextPaid.Start
precheck        = nextPaid.Start - 5 minuten
```

Er wordt dus geen `+1 seconde` toegepast over een gratis gat.

## Harde Visitgrenzen

Parkeerregels bepalen providerdekking, maar niet zelfstandig de functionele Visitduur. De Visit eindigt op de vroegste toepasselijke `DesiredEndAt`, elapsed-durationgrens of paid-durationgrens.

De paid-durationgrens wordt over de versioned rulesets berekend, zodat gratis/overnight tijd niet onterecht meetelt.

## Open-ended planning

Wanneer een Visit geen concrete eindtijd en geen eerdere harde policygrens heeft, gebruikt de scheduler een rolling zoek-/planningshorizon van 14 dagen. Dit is uitsluitend technische planning en geen parkeerregel of maximale Visitduur.
