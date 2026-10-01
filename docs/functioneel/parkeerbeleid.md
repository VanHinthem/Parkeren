# Parkeerbeleid

Het parkeerbeleid maakt expliciet onderscheid tussen gebruikersbeleid en gemeentelijke/providerregels.

## Gebruikersbeleid
`DefaultParkingPolicy` bevat de standaardwaarden. `UserPolicyOverride` bevat uitsluitend expliciete afwijkingen per gebruiker. `ParkingPolicyResolver` berekent daaruit een `EffectiveParkingPolicy`.

De Visit-policy bevat onder andere `MaxPaidParkingDuration`, optionele `MaxVisitElapsedDuration`, `AllowOpenEndedVisits`, `AllowVisitExtension` en `MaxConcurrentVisits`. `MaxProviderActionDuration` hoort bewust niet bij gebruikersbeleid; dit is onderdeel van de versioned `ParkingRuleSet`.

Voor de twee duurvelden heeft een user override drie expliciete modi:

- `Inherit` — gebruik de waarde uit `DefaultParkingPolicy`;
- `Value` — gebruik een concrete per-user duur;
- `Unlimited` — de effectieve duur is expliciet onbeperkt, ongeacht een begrensde default.

Na resolutie bevat `EffectiveParkingPolicy` alleen de effectieve waarde. Daar betekent `null` bij een duurveld altijd **onbeperkt**; de override-mode is alleen nodig om te bepalen hoe die effectieve waarde tot stand komt.

Handmatig stoppen is geen configureerbaar gebruikersrecht: een gebruiker moet een eigen actieve Visit altijd kunnen stoppen. `AllowOpenEndedVisits` bepaalt uitsluitend of `DesiredEndAt = null` is toegestaan. `AllowVisitExtension` bepaalt uitsluitend of een bestaande `DesiredEndAt` later mag worden gezet.

Bij het starten van een Visit wordt een immutable snapshot van de effective policy opgeslagen. Wijzigingen in defaults/overrides veranderen een reeds actieve Visit dus niet achteraf.

## Providerdekking
De applicatie verzorgt providerdekking automatisch volgens de toepasselijke `ParkingRuleSet`. Dit staat los van het recht van een gebruiker om een Visit te verlengen.

Voor Oss geldt momenteel:

```text
MaxProviderActionDuration = 4 uur
Continuation = StartNewAction
```

Live validatie van de gebruikte 2Park-interface heeft bevestigd dat `extend_action.json` wel `OK/SUCCESS` kan retourneren, maar de eindtijd van een actieve action niet persistent wijzigt. De 2Park-UI biedt voor zowel geplande als actieve actions eveneens geen verlengactie. Daarom gebruikt Oss `ExtendAction` niet als operationele strategie.

Wanneer een Visit wordt ingekort tot vóór het einde van een reeds actieve provider-action, wordt die action niet direct gestopt. De nieuwe `DesiredEndAt` wordt vastgelegd en er wordt duurzame scheduler-work ingepland die de provider-action op die eindtijd stopt en daarna de Visit afrondt.

## Tijd
Absolute Visit/provider-tijden worden als UTC-instants opgeslagen. Lokale parkeerregels worden in `Europe/Amsterdam` geëvalueerd.
