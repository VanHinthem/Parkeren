# SCHED-007 — `MaxPaidParkingDuration` grens

**Status:** ⚠️ Audit afgerond; grensberekening goed, lifecycle-afronding niet  
**Prioriteit:** hoog via SCHED-013

## Gewenste invariant

De totale betaalde tijd binnen één Visit mag de snapshotwaarde `MaxPaidParkingDuration` nooit overschrijden. Gratis tijd telt niet mee. Zodra de maximale betaalde duur is bereikt, mag geen nieuwe providerdekking worden gestart en moet de Visit-lifecycle naar een geldige eindtoestand bewegen.

## As-built gedrag

De processor berekent betaalde tijd met de geldige versioned rulesets. Hij vergelijkt zowel `paidThroughNow` als `paidThroughDesiredEnd` met de policygrens en gebruikt `FindPaidDurationBoundary` wanneer het gewenste planningsvenster de grens overschrijdt.

Daarmee wordt vervolgdekking correct afgekapt. De scheduler start niet bewust een nieuwe action voorbij de berekende betaalde-grens.

## Bevinding

Wanneer de grens is bereikt, wordt continuation-work `Completed`, maar er is in dit pad geen normale Visit-finalization. Hetzelfde probleem geldt wanneer het laatste providersegment precies op de betaalde limiet eindigt.

De limiet wordt dus gebruikt als **provider-planningsgrens**, maar niet volledig als **Visit-lifecyclegrens**. Dit is onderdeel van [SCHED-013](SCHED-013.md).

## Positief

- paid-time wordt gesegmenteerd; gratis perioden tellen niet mee;
- de policy is een immutable Visit-snapshot;
- de boundary wordt vóór volgende continuation opnieuw berekend;
- `MaxProviderActionDuration` blijft gescheiden van `MaxPaidParkingDuration`.

## Onduidelijkheden / open vragen

1. Moet het bereiken van `MaxPaidParkingDuration` functioneel dezelfde stop/finalization-flow gebruiken als een expliciete eindtijd, of een aparte automatische eindreden krijgen?
2. Welke `ActualEndAt` moet gelden wanneer de betaalde grens samenvalt met een gratis periode of provideractiongrens?

## Conclusie

De rekenkundige begrenzing ziet er correct uit. De ontbrekende overgang naar een terminale Visitstatus is het relevante betrouwbaarheidsprobleem en wordt centraal behandeld in SCHED-013.