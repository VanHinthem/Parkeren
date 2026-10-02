# SCHED-008 — `MaxVisitElapsedDuration` grens

**Status:** ⚠️ Audit afgerond; planning wordt begrensd, Visit-afronding ontbreekt  
**Prioriteit:** hoog via SCHED-013

## Gewenste invariant

Een Visit met `MaxVisitElapsedDuration` mag nooit voorbij `Visit.StartAt + MaxVisitElapsedDuration` doorlopen, ongeacht gratis perioden, extensions of rolling-horizon-logica.

## As-built gedrag

`ProviderCoverageSchedule.PlanningEndAt` gebruikt bij een open-ended Visit met een elapsed-limiet direct `Visit.StartAt + MaxVisitElapsedDuration`. De schedulerprocessor clampet `desiredEndAt` nogmaals op dezelfde harde grens en maakt geen vervolgdekking wanneer die grens bereikt is.

De start- en end-time validation gebruiken eveneens het policy-snapshot, zodat een gewone expliciete verlenging de elapsed-grens niet mag overschrijden.

## Bevinding

Zoals bij `MaxPaidParkingDuration` stopt de providerplanning wel, maar de scheduler maakt de Visit niet automatisch terminal wanneer de elapsed-grens wordt bereikt. In de productiecode is `Visit.Complete(...)` gekoppeld aan de duurzame Stop Visit-finalization; een normale harde elapsed-boundary doorloopt die flow niet vanzelf.

Dit is geen aparte rekenfout maar een lifecycleprobleem; zie [SCHED-013](SCHED-013.md).

## Positieve beschermingen

- de grens is gebaseerd op de oorspronkelijke `Visit.StartAt`; verlengen reset hem niet;
- de snapshot voorkomt dat latere policywijzigingen de actieve Visit stilzwijgend veranderen;
- continuation wordt niet bewust voorbij de harde grens gepland.

## Onduidelijkheden / open vragen

1. Moet een elapsed-policygrens een eigen automatische stopreden/auditveld krijgen?
2. Moet de automatische terminale flow enkele minuten vóór de harde grens al voorbereiden wanneer nog een actieve provideraction bestaat, of volstaat een durable stop op exact de grens?

## Conclusie

De tijdgrens wordt in planning correct gerespecteerd, maar zonder betrouwbare Visit-finalization blijft een Visit lokaal capaciteit bezetten nadat de policygrens is verstreken. Oplossen via het gedeelde lifecycle-ontwerp van SCHED-013.