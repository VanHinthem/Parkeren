# SCHED-001 — T-5 en aansluitende `StartNewAction`

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Scenario:** aaneengesloten betaald parkeren met een opvolgende provideraction  
**Datum bevinding:** 2 oktober 2026

## Samenvatting

De gewenste V1-strategie is inmiddels door echte 2Park-tests bevestigd: rond T-5 mag één toekomstige successor worden aangemaakt met:

```text
Start = predecessor.End + 1 seconde
status bij provider = scheduled
```

De huidige scheduler plant het work wel op T-5, maar `ProviderContinuationStartStore.PrepareAttemptAsync(...)` weigert de successor zolang de predecessor nog niet is afgelopen. Daardoor wordt T-5 feitelijk een exception/retry-cyclus tot rond de providergrens in plaats van het afgesproken JIT-schedulingmoment.

## Bevestigd 2Park-contract uit #71

Live getest op 29 september 2026:

- een action vijf minuten in de toekomst krijgt direct een provider action-id en status `scheduled`;
- die scheduled action kan vóór start worden geannuleerd;
- `B.Start == A.End` wordt geweigerd met `PRK-00005` wegens overlap;
- `B.Start = A.End + 1 seconde` wordt geaccepteerd terwijl A nog `active` is en B `scheduled` wordt;
- V1-besluit: continuation just-in-time vanaf T-5, maximaal één scheduled successor per Visit.

De providersemantiek is dus geen open ontwerpvraag meer: de huidige code wijkt af van een reeds bewezen en vastgelegde V1-strategie.

## As-built flow

Na een bevestigde eerste provideraction maakt de applicatie `ContinueProviderCoverage` met:

```text
DueAt = predecessor.PlannedEndAt - 5 minuten
```

De scheduler claimt dit durable work, controleert de actuele provideraction en gebruikt `work.Id` als stabiel operation-id voor continuation.

Daarna roept de processor `ProviderContinuationStartStore.PrepareAttemptAsync` aan. Deze store bevat echter:

```text
als predecessor.PlannedEndAt > UtcNow
    -> InvalidOperationException
```

Tegelijkertijd bouwt dezelfde store de nieuwe action als:

```text
PlannedStartAt = predecessor.PlannedEndAt + 1 seconde
```

Praktisch gevolg:

```text
T-5 -> exception -> work ongeveer +1 minuut opnieuw Pending
T-4 -> exception
T-3 -> exception
T-2 -> exception
T-1 -> exception
rond T -> pas dan mag PrepareAttemptAsync verder
```

De `ProviderContinuationStartMutationGuard` is juist al ontworpen om een toekomstige action tot ongeveer vijf minuten vooruit toe te staan. De store en mutation guard spreken elkaar daarmee intern tegen.

## Tweede inconsistentie: scheduled read-back

Zelfs wanneer de store wordt aangepast zodat de T-5 successor wél extern gestart kan worden, accepteert `StartVisitProviderExecutor` de directe read-back momenteel alleen wanneer de providerstatus `active` is.

Een correct op T-5 aangemaakte 2Park-successor is volgens de live test echter `scheduled`. De continuation-resultstore en reconciler kunnen `scheduled` wel verwerken, maar de directe executor zal de geldige mutation eerst als `Unknown` markeren.

De uiteindelijke oplossing moet dus de volledige keten uitlijnen:

```text
T-5 planning
-> persisted ContinueStart operation/action
-> future provider Start
-> scheduled read-back accepteren
-> work wacht tot activation boundary
-> scheduled -> active bevestigen
-> volgende continuation plannen
```

## Betrouwbaarheidsimpact

De huidige implementation maakt naadloze coverage afhankelijk van worker/retry/providerlatency precies rond de eindgrens, terwijl de provider juist vooraf plannen ondersteunt. Dit verkleint de beschikbare recoverymarge en is in strijd met de afgesproken reden voor T-5.

## Positieve bouwstenen

- persistent schedulerwork;
- `FOR UPDATE SKIP LOCKED` voor claiming;
- stabiele operation-id via `work.Id`;
- durable `ProviderOperation`;
- provider precheck vóór continuation;
- unknown/reconciliation in plaats van blind retry;
- max één latere lokale action wordt bewaakt.

Deze onderdelen hoeven conceptueel niet vervangen te worden.

## Relaties

- [SCHED-002](SCHED-002.md): hetzelfde pre-schedulingprincipe bij hervatten na gratis tijd.
- [SCHED-009](SCHED-009.md): unknown/reconciliation.
- [SCHED-014](SCHED-014.md): claim/Visit locking.
- [SCHED-016](SCHED-016.md): mock ondersteunt de tijdsstatusovergang onvoldoende.
- [SCHED-017](SCHED-017.md): provider timestamps mogen niet op vrijwel exacte gelijkheid worden gematcht.

## Onduidelijkheden / open vragen

1. Hoe en wanneer verandert echte 2Park een `scheduled` action in read-back naar `active`?
2. Moet de scheduler na succesvolle T-5 scheduling één workitem hergebruiken om activation te controleren, of een apart activation/reconciliation worktype gebruiken?
3. Welke marge rond de geplande start is acceptabel voor het bevestigen van activatie?
4. Telt een scheduled successor mee voor de provider-capaciteitslimiet? Dit staat nog open in #71.

## Verificatiecriteria na fix

SCHED-001 kan pas op ✅ wanneer bewezen is:

1. T-5 creëert zonder exception-polling precies één scheduled successor;
2. Start = predecessor.End + 1 seconde;
3. `scheduled` read-back wordt als geldige succesvolle mutation opgeslagen;
4. Stop kan de scheduled successor veilig annuleren;
5. restart/timeout veroorzaakt geen duplicate successor;
6. activation rond de grens wordt correct lokaal verwerkt;
7. integratietests gebruiken een provider/mock die de relevante tijdsstatussen realistisch modelleert.