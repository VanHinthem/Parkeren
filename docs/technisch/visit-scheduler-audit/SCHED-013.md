# SCHED-013 — natuurlijke Visit-afronding ontbreekt

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** kritiek/hoog  
**Raakt:** normale finite Visits, free-only Visits, SCHED-007, SCHED-008 en provider-reconciliation.

## Samenvatting

De scheduler heeft meerdere paden die correct bepalen dat **geen verdere providerdekking meer nodig is**, maar dat is niet hetzelfde als het beëindigen van de logische `Visit`.

In de huidige productiecode is de expliciete overgang naar `VisitStatus.Completed` gekoppeld aan `StopVisitFinalizer`. Een normale finite Visit die zijn `DesiredEndAt` bereikt zonder expliciete Stop-flow krijgt niet vanzelf zo'n finalization-pad.

## Bewijs uit de code

- `VisitStartStore` plant Long Visit work en eventueel toekomstige providercoverage, maar geen generiek terminal `StopVisit` op `DesiredEndAt`.
- `ProviderStartResultStore` plant alleen continuation wanneer later nog betaalde dekking nodig is.
- `VisitSchedulerWorkProcessor` markeert continuation-work op diverse eindgrenzen als `Completed`, maar completeert daarmee alleen het **workitem**, niet de Visit.
- `StopVisit` scheduler-work wordt specifiek aangemaakt bij active shortening wanneer een actieve provideraction voorbij de nieuwe eindtijd loopt.
- in de productiecode is `visit.Complete(...)` gekoppeld aan `StopVisitFinalizer`.
- startup recovery kan continuation-work herbouwen, maar er is geen generiek gevonden pad dat een gewone verlopen finite Visit op `DesiredEndAt` alsnog via Stop/finalization afrondt.

## Concrete gevolgen

### Finite paid Visit

Als de laatste provideraction precies eindigt op `DesiredEndAt`, kan daarna geen continuation meer nodig zijn. De lokale Visit kan echter `Active` blijven.

### Free-only of free-tail Visit

Een Visit die in gratis tijd loopt en geen provideraction nodig heeft, kan eveneens `Active` blijven nadat de gewenste eindtijd voorbij is.

### Harde policygrenzen

Bij `MaxPaidParkingDuration` en `MaxVisitElapsedDuration` stopt de scheduler met nieuwe providerdekking, maar zonder aparte terminal flow blijft de logische Visit potentieel actief. Zie SCHED-007 en SCHED-008.

## Betrouwbaarheidsimpact

Een verlopen maar lokaal actieve Visit kan:

- user/global concurrencycapaciteit blijven bezetten;
- als actieve Visit op dashboard/API zichtbaar blijven;
- Long Visit warnings blijven genereren;
- latere recovery/discrepancy-logica in een toestand brengen waarin providerstate en Visitstate niet meer overeenkomen;
- een gebruiker verhinderen een nieuwe Visit te starten wanneer diens max concurrency 1 is.

Dit is daarom geen cosmetisch statusprobleem maar een lifecycle-invariant.

## Gewenste invariant

Iedere Visit moet een durable terminale lifecycle hebben voor alle eindredenen:

- handmatige Stop;
- `DesiredEndAt` bereikt;
- `MaxVisitElapsedDuration` bereikt;
- `MaxPaidParkingDuration` bereikt waar dat de functionele Visit beëindigt;
- eventueel andere automatische harde grenzen.

Daarbij moet eventuele actieve/scheduled providerdekking eerst veilig zijn afgehandeld.

## Onduidelijkheden / open vragen

1. Willen we één generieke `EndVisit`/Stop-flow hergebruiken voor natuurlijke eindes, of aparte scheduler-work met een expliciete eindreden?
2. Moet bij een finite Visit altijd vooraf terminal work op de Visit-eindtijd bestaan, ook wanneer geen provideraction loopt?
3. Welke `ActualEndAt` gebruiken we wanneer provider timestamps enkele seconden afwijken: functionele Visitgrens of providerreadback?
4. Moet `MaxPaidParkingDuration` de hele Visit beëindigen, of alleen verdere betaalde dekking blokkeren terwijl een gratis staart nog functioneel toegestaan zou zijn? Dit moet productmatig expliciet worden vastgelegd.

## Richting voor later ontwerp

Nog geen fix in deze audit. Wel lijkt het verstandig providerdekking en Visit-finalization als twee expliciete verantwoordelijkheden te modelleren: “coverage klaar” mag nooit impliciet betekenen “Visit lifecycle klaar”.

## Verificatiecriteria

Na een oplossing moeten tests minimaal aantonen dat paid, free-only, overnight-tail en policy-boundary Visits automatisch een terminale status krijgen en daarna geen capaciteit of schedulerwork achterlaten.