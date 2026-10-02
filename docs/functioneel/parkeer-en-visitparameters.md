# Parkeer- en Visitparameters

Deze pagina legt de functionele scheiding vast tussen Visit-policy, providerproduct en parkeer/providerregels.

## 1. Visit-policy

De effectieve policy wordt bij start als immutable snapshot op de Visit vastgelegd.

| Parameter | Betekenis |
| --- | --- |
| `MaxPaidParkingDuration` | Maximale totale betaalde parkeertijd binnen één Visit. Gratis tijd telt niet mee. `null` = onbeperkt. |
| `MaxVisitElapsedDuration` | Maximale verstreken tijd vanaf `Visit.StartAt`. `null` = onbeperkt. |
| `AllowOpenEndedVisits` | Staat `DesiredEndAt = null` toe. |
| `AllowVisitExtension` | Staat verlengen van een concrete `DesiredEndAt` toe. |
| `MaxConcurrentVisits` | Maximaal aantal gelijktijdige Visits voor die gebruiker. |

Bij user overrides ondersteunen de duurvelden `Inherit`, `Value` en `Unlimited`.

### Handmatig stoppen

Handmatig stoppen is geen optioneel gebruikersrecht: een gebruiker moet zijn eigen actieve Visit altijd kunnen stoppen. `AllowOpenEndedVisits` bepaalt alleen of vooraf een eindtijd verplicht is.

### Terminale Visitgrens

De vroegste toepasselijke grens uit `DesiredEndAt`, elapsed-duration en paid-duration beëindigt de hele Visit. Dit is dus meer dan alleen een grens voor nieuwe providerdekking.

## 2. Providerproduct

Een Visit blijft gedurende zijn lifecycle gekoppeld aan het providerproduct waarmee hij gestart is. Defaultproductwijzigingen hebben geen invloed op actieve of historische Visits.

De provider-location hoort bij het product en wordt niet als zelfstandige gebruikerszone behandeld.

## 3. Parkeer-/providerregels

Een versioned `ParkingRuleSet` bepaalt per providerproduct:

| Parameter | Betekenis |
| --- | --- |
| `ValidFrom` / `ValidUntil` | Geldigheidsperiode van de ruleset. |
| `PaidWindows` | Betaalde weekdag/tijdvensters. |
| `CalendarExceptions` | Datumgebonden betaald/gratis uitzonderingen. |
| `PublicHolidaysAreFree` | Nederlandse feestdagen standaard gratis indien ingesteld. |
| `MaxProviderActionDuration` | Maximale duur van één provideraction; niet van de Visit. |
| `Continuation` | `StartNewAction` of `ExtendAction`. |

Regels worden geëvalueerd in `Europe/Amsterdam`; persistente absolute timestamps zijn UTC-instants.

## Oss / 2Park

Voor Oss is de operationele V1-strategie:

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
```

Live tests bevestigden:

- 241 minuten wordt door 2Park geweigerd met maximale-duurvalidatie;
- overlap wordt geweigerd (`PRK-00005`);
- een future action kan als `scheduled` worden aangemaakt;
- voor aansluitende betaalde acties werkt `successor.Start = predecessor.End + 1 seconde`;
- provider-extend gaf geen betrouwbaar persistent gewijzigd einde en wordt daarom voor Oss niet gebruikt.

De scheduler plant aaneengesloten continuation JIT vanaf T-5. Na een gratis gat wordt het volgende betaalde segment eveneens op T-5 voorbereid, maar begint de nieuwe provideraction exact op `nextPaid.Start`.

## Provider timestamp matching

Provider read-back mag enkele seconden afwijken van de lokaal geplande tijden. V1 gebruikt centraal **5 seconden tolerance** voor Start en End.

Die 5 seconden zijn een **engineering margin**, geen gemeten 2Park-SLA. Bekende provider action-id en productcontext blijven leidend; zonder bekende action-id is alleen een unieke fallbackmatch toegestaan.

## Open-ended Visits

Een open-ended Visit heeft `DesiredEndAt = null`. Als ook beide harde duurgrenzen `null` zijn, heeft de Visit geen vooraf bekende functionele eindtijd.

De scheduler gebruikt dan een rolling horizon van 14 dagen om vooruit te plannen. Die horizon wordt opnieuw verschoven zolang de Visit actief blijft en is geen functionele limiet.

## Belangrijkste scheiding

```text
Visit-policy              => wat de Visit mag en wanneer hij eindigt
ParkingRuleSet            => wanneer providerdekking nodig is en hoe die wordt uitgevoerd
Providerproduct           => externe productcontext van de Visit
Scheduler                 => duurzame tijdgestuurde uitvoering van bovenstaande regels
```

Geen van deze lagen mag stilzwijgend de verantwoordelijkheid van een andere laag overnemen.
