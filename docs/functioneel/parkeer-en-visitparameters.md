# Parkeer- en Visitparameters

Deze pagina legt de functionele betekenis en verantwoordelijkheid vast van parameters die bepalen **hoe lang een Visit mag lopen**, **wat een gebruiker ermee mag doen** en **hoe de benodigde 2Park-providerdekking wordt uitgevoerd**.

## Verantwoordelijkheden

Er zijn twee verschillende niveaus:

1. **Gebruikersbeleid / Visit-policy** bepaalt wat een gebruiker met een Visit mag en welke grenzen voor de totale Visit gelden.
2. **Parkeer-/providerregels** bepalen wanneer parkeren betaald is en hoe de applicatie daarvoor 2Park-actions moet gebruiken.

Een providerlimiet hoort niet in gebruikersbeleid. Andersom hoort een gebruikersrecht, zoals een Visit mogen verlengen, niet in de 2Park-regels.

## Gebruikersbeleid en Visit

| Parameter | Type | Betekenis |
| --- | --- | --- |
| `MaxPaidParkingDuration` | `TimeSpan?` | Maximale totale **betaalde parkeertijd** binnen één Visit. Gratis perioden tellen niet mee. `null` betekent **geen maximale betaalde parkeertijd**. |
| `MaxVisitElapsedDuration` | `TimeSpan?` | Maximale verstreken tijd vanaf de oorspronkelijke `Visit.StartAt`. `null` betekent **geen maximale totale Visitduur**. Verlengen reset deze teller niet. |
| `AllowOpenEndedVisits` | `bool` | Bepaalt of een gebruiker een Visit zonder vooraf gekozen eindtijd mag starten. Bij `false` is `DesiredEndAt` verplicht. Bij `true` mag `DesiredEndAt = null` zijn. |
| `AllowVisitExtension` | `bool` | Bepaalt of een gebruiker de `DesiredEndAt` van een reeds actieve Visit naar een later tijdstip mag wijzigen. |
| `MaxConcurrentVisits` | `int` | Maximaal aantal gelijktijdig actieve Visits voor deze gebruiker. |

Bij `UserPolicyOverride` worden de twee duurvelden aangevuld met een `PolicyDurationOverrideMode`:

- `Inherit`: default volgen;
- `Value`: concrete overridewaarde gebruiken;
- `Unlimited`: expliciet geen limiet.

Hierdoor blijft `null` in de uiteindelijke `EffectiveParkingPolicy` ondubbelzinnig: het betekent daar altijd **onbeperkt**.

### Handmatig stoppen

Handmatig stoppen is **geen configureerbaar gebruikersrecht**. Een gebruiker moet een eigen actieve Visit altijd kunnen stoppen. Een beheerder kan een Visit stoppen wanneer diens autorisatie dat toestaat.

### Open-ended Visit

Een Visit is open-ended wanneer:

```text
DesiredEndAt = null
AllowOpenEndedVisits = true
```

De Visit heeft dan geen vooraf gekozen eindtijd en loopt totdat hij expliciet wordt gestopt of een toepasselijke harde limiet wordt bereikt.

Wanneer ook:

```text
MaxVisitElapsedDuration = null
```

geldt er vanuit de Visit-policy **geen maximale verstreken Visitduur**. De parkeerregels en eventuele andere harde limieten blijven wel van toepassing.

### Visit verlengen

Bij een actieve Visit met een concrete `DesiredEndAt` mag de gebruiker de eindtijd alleen naar later wijzigen wanneer:

```text
AllowVisitExtension = true
```

Een verlenging blijft onder alle geldende limieten vallen. Indien `MaxVisitElapsedDuration` niet `null` is:

```text
new DesiredEndAt <= Visit.StartAt + MaxVisitElapsedDuration
```

Ook `MaxPaidParkingDuration` (wanneer niet `null`) en de toepasselijke parkeerregels moeten opnieuw worden gevalideerd.

Het automatisch verzorgen van providerdekking is geen gebruikersrecht en staat los van het handmatig verlengen van een Visit.

## Parkeer- en 2Park-providerregels

Deze waarden horen bij de versioned `ParkingRuleSet`, omdat ze door gemeente/parkeergebied/provider kunnen verschillen.

| Parameter | Type | Betekenis |
| --- | --- | --- |
| `ValidFrom` | `DateTimeOffset` | Moment vanaf wanneer deze ruleset geldig is. |
| `ValidUntil` | `DateTimeOffset?` | Optioneel einde van de geldigheid. `null` betekent geen vooraf bepaald einde. |
| `PaidWindows` | collectie | Weekdag + begin/eindtijd waarop parkeren betaald is. Buiten deze vensters is geen providerdekking nodig, behoudens uitzonderingen. |
| `CalendarExceptions` | collectie | Datumgebonden uitzondering die expliciet bepaalt of die datum/tijd als betaald of gratis wordt behandeld. |
| `PublicHolidaysAreFree` | `bool` | Bepaalt of Nederlandse feestdagen standaard als gratis worden behandeld. |
| `MaxProviderActionDuration` | `TimeSpan` | Maximale duur van één afzonderlijke 2Park/provider-action. Dit is **niet** de maximale duur van de logische Visit. |
| `Continuation` | enum | Bepaalt hoe providerdekking na het einde van een action wordt voortgezet: `StartNewAction` of `ExtendAction`. |

Parkeerregels worden in business-timezone `Europe/Amsterdam` geëvalueerd; opgeslagen absolute tijdstippen blijven UTC-instants.

### Continuation

`Continuation` gaat uitsluitend over **hoe** de applicatie aansluitende providerdekking realiseert:

- `StartNewAction`: start een nieuwe aansluitende 2Park-action.
- `ExtendAction`: verleng de bestaande provider-action via de provider-API.

Dit staat los van `AllowVisitExtension`. Als een geldige Visit bijvoorbeeld tot 17:00 loopt en de huidige provider-action om 16:00 eindigt, is het de verantwoordelijkheid van de applicatie om 16:00–17:00 providerdekking te verzorgen volgens de actieve `ParkingRuleSet`.

Voor Oss is het huidige model:

```text
MaxProviderActionDuration = 4 uur
Continuation = StartNewAction
```

Een Visit van zes betaalde uren kan daardoor uit meerdere 2Park-actions bestaan zonder dat de logische Visit wordt onderbroken.

### Bevestigd 2Park-gedrag voor Oss

De gebruikte 2Park-interface exposeert `extend_action.json`, maar live tests op een actieve action lieten zien dat een `OK/SUCCESS` response de eindtijd niet persistent wijzigde, ook niet bij een JIT-poging één minuut voor het einde. De 2Park-UI biedt voor geplande en actieve actions alleen verwijderen/stoppen en geen verlengen.

Daarom is voor Oss `Continuation = StartNewAction` de operationele strategie. Een volgende action wordt JIT gestart; wegens de door 2Park bevestigde overlapcontrole begint een successor na de vorige provider-eindtijd.

Als een gebruiker de `DesiredEndAt` verkort tot een tijdstip vóór het einde van de actieve provider-action, kan de provider-eindtijd niet worden aangepast. De applicatie plant daarom duurzame `StopVisit` scheduler-work op de nieuwe `DesiredEndAt`. Tot dat moment blijft de bestaande action actief; op de gewenste eindtijd wordt hij gestopt en wordt de Visit afgerond.

## Belangrijke scheiding

Voorbeeld:

```text
Visit.StartAt              = 12:00
Visit.DesiredEndAt         = 18:00

MaxVisitElapsedDuration    = 8 uur
MaxPaidParkingDuration     = 8 uur  # null = onbeperkt
AllowOpenEndedVisits       = true
AllowVisitExtension        = true

MaxProviderActionDuration  = 4 uur
Continuation               = StartNewAction
```

De Visit mag tot 18:00 lopen. De providerregel kan daarvoor bijvoorbeeld twee aansluitende 2Park-actions nodig maken. Dat is een technisch/reglementair detail van de providerdekking en verandert de Visit zelf niet.

## Policy snapshot

Bij het starten van een Visit wordt het effectieve gebruikersbeleid als immutable snapshot bij de Visit opgeslagen. Latere wijzigingen aan defaults of user overrides mogen een reeds actieve Visit niet stilzwijgend veranderen.

Parkeerregels zijn versioned en worden toegepast op basis van hun geldigheidsperiode, zodat ook een Visit die over een regelgrens loopt correct kan worden gesegmenteerd.
