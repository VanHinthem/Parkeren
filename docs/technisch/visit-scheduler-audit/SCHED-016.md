# SCHED-016 — TwoParkMock modelleert natuurlijke tijdsstatussen niet

**Status:** ⚠️ Testmodel-bevinding bevestigd  
**Prioriteit:** middel/hoog  
**Raakt:** SCHED-001, SCHED-002, SCHED-011 en scheduler end-to-end bewijs.

## Samenvatting

`TwoParkMock` bepaalt bij het aanmaken één keer de status:

```text
start > now  -> scheduled
anders       -> active
```

Daarna verandert die status niet automatisch wanneer de klok de geplande start of eindtijd passeert. Een scheduled action blijft zonder expliciete testmutatie `scheduled`; een active action blijft lokaal in de mock `active` nadat `End` verstreken is.

## Waarom dit relevant is

De echte scheduler bevat juist branches die afhangen van tijdsgedreven providerstatussen:

- scheduled successor wordt later `active`;
- predecessor eindigt natuurlijk;
- overnight/free-gap hervatting beoordeelt de vorige remote action;
- discrepancy-detectie interpreteert ontbrekende of niet-active actions.

Een integratietest tegen de huidige mock kan daarom lokale schedulerlogica groen maken terwijl de echte provider rondom de tijdsgrens ander gedrag vertoont.

Voorbeeld: een test kan na `End` nog een remote action met status `active` terugkrijgen, omdat de mock geen natuurlijke expiratie simuleert. Dat bewijst niet dat echte 2Park dezelfde state teruggeeft.

## Impact

Dit is geen productiebug op zichzelf, maar een betrouwbaarheidsrisico in onze bewijsvoering. Juist de scheduler is tijdgedreven; een statisch providerstatusmodel mist daardoor de belangrijkste transitions.

## Gewenste mocksemantiek

Het mockcontract moet uiteindelijk voldoende realistisch zijn om minimaal te modelleren:

- `scheduled` vóór Start;
- `active` vanaf Start tot End;
- een expliciet gekozen natuurlijke toestand na End die aansluit bij bevestigd 2Park-gedrag;
- stop/cancel vóór en tijdens actief parkeren;
- zichtbaarheid/read-back volgens configureerbare providersemantiek waar nodig.

De natuurlijke post-End-status moet eerst live worden bevestigd; zie SCHED-002.

## Onduidelijkheden / open vragen

1. Welke status/zichtbaarheid heeft een echte 2Park-action na natuurlijke afloop?
2. Moet de mock dat dynamisch berekenen bij read-back of via een achtergrondtransitie persistenteren?
3. Hebben we voor tests een injecteerbare klok nodig in TwoParkMock om boundaries deterministic te testen?

## Conclusie

De mock is bruikbaar voor mutation- en foutscenario's, maar nog onvoldoende als bewijs voor scheduler-time-boundary gedrag. Verbetering hiervan hoort vóór de uiteindelijke scheduler-hardeningtests.