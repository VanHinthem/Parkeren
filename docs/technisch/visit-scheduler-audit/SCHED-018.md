# SCHED-018 — Visit scheduler observability / audit trail

**Status:** 📋 Gereed om later op te pakken; nog niet gestart  
**Prioriteit:** middel/hoog  
**Raakt:** alle schedulerflows, beheerdiagnostiek en troubleshooting.

## Doel

Per Visit een persistente, chronologische scheduler/audit-timeline beschikbaar maken waarmee achteraf betrouwbaar kan worden vastgesteld **wat de scheduler heeft gedaan, waarom dat gebeurde en welke provider-/Visit-state daarbij hoorde**.

Dit is nadrukkelijk iets anders dan gewone applicatie-/structured logging. Serverlogs blijven bedoeld voor technische runtime-diagnostiek; de Visit scheduler audit trail wordt een domeingerichte historie die aan één Visit gekoppeld blijft.

## Waarom pas nu

De schedulerstate-machine en reliability-hardening voor SCHED-001 t/m SCHED-017 zijn inmiddels geïmplementeerd en opnieuw geverifieerd. Daarmee is de functionele basis stabiel genoeg om een observabilitycontract te kunnen ontwerpen zonder tijdelijke schedulerpaden als permanent auditcontract vast te leggen.

**Er is nog geen observability-datamodel, eventcatalogus of implementatie gestart.** Eerst wordt de bestaande functionele en technische documentatie gelijkgetrokken en beoordeeld; daarna wordt expliciet besloten of SCHED-018 wordt gestart.

## Gewenste eigenschappen

De toekomstige audit trail moet:

- persistent en restartbestendig zijn;
- chronologisch per Visit opvraagbaar zijn;
- betekenisvolle domein-/schedulertransities vastleggen, niet iedere interne logregel;
- scheduler work, provider operations en provider actions kunnen correleren;
- retries en reconciliation verklaarbaar maken;
- voldoende context bevatten om een incident te reconstrueren zonder losse serverlogs te combineren;
- geen secrets, providercredentials of onnodige persoonsgegevens opslaan;
- geschikt zijn om later in beheer als Visit-timeline te tonen.

## Waarschijnlijke eventcategorieën

De definitieve lijst moet nog worden ontworpen. Kandidaten zijn onder andere:

- scheduler-work aangemaakt, geclaimd, uitgesteld, voltooid of geannuleerd;
- terminale Visit-boundary gepland/gewijzigd/bereikt;
- continuation gepland;
- provideraction voorbereid / scheduled / active / gestopt / completed;
- manual Stop die schedulerwerk of providerdekking beïnvloedt;
- `DesiredEndAt` wijziging met vervangen/geannuleerd work;
- gratis/betaald overgang wanneer dit scheduleractie veroorzaakt;
- harde policygrens bereikt;
- retry/reschedule inclusief reden;
- provider mutation met unknown outcome;
- reconciliation gestart en resultaat;
- startup recovery / scheduler rebuild;
- discrepancy / `AttentionRequired`;
- automatische Visit-finalization.

## Waarschijnlijke context

Een event zal waarschijnlijk minimaal correleren met:

- `VisitId`;
- timestamp;
- eventtype;
- reden/beschrijving;
- `VisitSchedulerWorkId` indien relevant;
- `ProviderOperationId` indien relevant;
- `ProviderParkingActionId` / externe provider action-id indien relevant;
- relevante lifecycle/health/work/provider state;
- beperkte reconstructiemetadata.

De exacte velden, retentie en append-only invarianten zijn nog niet besloten.

## Open ontwerpvragen

1. Eigen persistente entiteit, uitbreiding van bestaand eventmodel, of projectie uit bestaande durable records?
2. Welke events zijn permanent functioneel betekenisvol en welke blijven alleen structured logs?
3. Welke metadata is sterk getypeerd en welke compacte details mogen flexibel zijn?
4. Welke retentie geldt voor scheduler audit events?
5. Wie mag de timeline zien: alleen beheerder of gedeeltelijk ook gebruiker?
6. Hoe grof/fijn tonen we recovery/reconciliation?
7. Worden events append-only en hoe dwingen we dat af?
8. Hoe voorkomen we duplicate audit events bij replay/idempotency?

## Verificatiecriteria

SCHED-018 kan pas naar ✅ wanneer minimaal bewezen is dat:

1. iedere belangrijke schedulerflow een begrijpelijke Visit-timeline oplevert;
2. retries/recovery/reconciliation causaal te reconstrueren zijn;
3. events niet dubbel ontstaan door replay/idempotency;
4. de audit trail restartbestendig is;
5. beheer de historie per Visit kan raadplegen;
6. de audit trail geen secrets of onnodige gevoelige data bevat.

## Startvoorwaarde

SCHED-018 wordt pas inhoudelijk ontworpen of geïmplementeerd na een expliciete vervolgbeslissing na afronding en beoordeling van de huidige documentatieronde.
