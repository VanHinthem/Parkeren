# Parkeerbeleid

Het parkeerbeleid maakt expliciet onderscheid tussen gebruikersbeleid en gemeentelijke/providerregels.

## Gebruikersbeleid

`DefaultParkingPolicy` bevat standaardwaarden. `UserPolicyOverride` bevat expliciete afwijkingen per gebruiker. `ParkingPolicyResolver` berekent daaruit de effectieve policy die bij Visit-start als immutable snapshot wordt opgeslagen.

De Visit-policy bevat onder andere:

- `MaxPaidParkingDuration`;
- `MaxVisitElapsedDuration`;
- `AllowOpenEndedVisits`;
- `AllowVisitExtension`;
- `MaxConcurrentVisits`.

`MaxProviderActionDuration` hoort niet bij gebruikersbeleid maar bij de versioned `ParkingRuleSet`.

Voor duurvelden ondersteunen user overrides:

- `Inherit`;
- `Value`;
- `Unlimited`.

In de uiteindelijke effectieve policy betekent `null` bij een duurveld altijd onbeperkt.

## Stoppen en verlengen

Handmatig stoppen is geen configureerbaar gebruikersrecht: een gebruiker moet zijn eigen actieve Visit altijd kunnen stoppen.

`AllowOpenEndedVisits` bepaalt alleen of `DesiredEndAt = null` is toegestaan. `AllowVisitExtension` bepaalt alleen of een bestaande concrete eindtijd later mag worden gezet.

## Harde Visitgrenzen

De effectieve terminale Visitgrens is de vroegste toepasselijke grens uit:

1. `DesiredEndAt`;
2. `Visit.StartAt + MaxVisitElapsedDuration`;
3. het moment waarop `MaxPaidParkingDuration` is verbruikt.

`MaxPaidParkingDuration` en `MaxVisitElapsedDuration` beëindigen dus de hele Visit; zij blokkeren niet alleen nieuwe providerdekking.

## Providerdekking

De applicatie verzorgt providerdekking automatisch volgens de toepasselijke `ParkingRuleSet`. Voor Oss geldt:

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
```

Live validatie liet zien dat provider-extend geen betrouwbaar persistent gewijzigd einde opleverde. Oss gebruikt daarom geen `ExtendAction` als operationele continuationstrategie.

Aaneengesloten betaalde continuation wordt JIT voorbereid op T-5 en start wegens 2Park-overlapcontrole op predecessor.End + 1 seconde. Na een gratis gat wordt het volgende betaalde segment eveneens op T-5 voorbereid, maar start de provideraction exact op `nextPaid.Start`.

## Verkorten

Wanneer een Visit wordt ingekort, wordt de terminale boundary opnieuw berekend. Scheduled providerdekking die niet meer past wordt geannuleerd/vervangen en providerdekking die voorbij de nieuwe grens loopt wordt op die grens via duurzame Stop-afhandeling beëindigd.

## Open-ended

Een volledig open-ended Visit zonder harde duurgrenzen heeft geen vooraf bekende functionele eindtijd. De scheduler gebruikt technisch een rolling horizon van 14 dagen; die horizon is geen beleidslimiet.

## Tijd en provider matching

Absolute Visit/provider-tijden worden als UTC-instants opgeslagen. Lokale parkeerregels worden in `Europe/Amsterdam` geëvalueerd.

Provider read-back gebruikt centraal 5 seconden Start/End tolerance waar timestamps voor matching relevant zijn. Dit is een engineering margin en geen gemeten 2Park-SLA.
