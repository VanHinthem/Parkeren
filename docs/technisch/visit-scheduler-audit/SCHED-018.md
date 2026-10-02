# SCHED-018 — Visit scheduler observability / audit trail

**Status:** 🛠 Ontwerp gepland na schedulerfixes  
**Prioriteit:** middel/hoog  
**Raakt:** alle schedulerflows, beheerdiagnostiek en troubleshooting.

## Doel

Per Visit een persistente, chronologische scheduler/audit-timeline beschikbaar maken waarmee achteraf betrouwbaar kan worden vastgesteld **wat de scheduler heeft gedaan, waarom dat gebeurde en welke provider-/Visit-state daarbij hoorde**.

Dit is nadrukkelijk iets anders dan gewone applicatie-/structured logging. Serverlogs blijven bedoeld voor technische runtime-diagnostiek; de Visit scheduler audit trail wordt een domeingerichte historie die aan één Visit gekoppeld blijft.

## Waarom als laatste verbeterpunt

De eventcatalogus moet de definitieve scheduler-state-machine volgen. Daarom wordt SCHED-018 pas inhoudelijk ontworpen en geïmplementeerd nadat de schedulerbevindingen SCHED-001 t/m SCHED-017 zijn opgelost of expliciet geaccepteerd.

Daarmee voorkomen we dat tijdelijke of straks verwijderde schedulerpaden onderdeel worden van een publiek/beheerbaar auditcontract.

## Gewenste eigenschappen

De audit trail moet:

- persistent zijn en een proces/container-restart overleven;
- chronologisch per Visit opvraagbaar zijn;
- betekenisvolle domein-/schedulertransities vastleggen, niet iedere interne logregel;
- scheduler work, provider operations en provider actions kunnen correleren;
- retries en reconciliation verklaarbaar maken;
- voldoende context bevatten om een incident te reconstrueren zonder losse serverlogs te moeten combineren;
- geen secrets, providercredentials of onnodige persoonsgegevens opslaan;
- geschikt zijn om later in beheer als Visit-timeline te tonen.

## Voorlopige eventcategorieën

De definitieve lijst wordt pas na de schedulerfixes vastgesteld. Waarschijnlijke categorieën zijn:

- scheduler-work aangemaakt, geclaimd, vrijgegeven, voltooid of geannuleerd;
- terminal Visit-work gepland en uitgevoerd;
- continuation gepland;
- provideraction voorbereid / scheduled / active / gestopt / completed;
- handmatige Stop die schedulerwerk of providerdekking beïnvloedt;
- `DesiredEndAt` wijziging met vervangen/geannuleerd schedulerwerk;
- gratis → betaald of betaald → gratis overgang wanneer dit scheduleractie veroorzaakt;
- harde policygrens bereikt;
- retry/reschedule inclusief reden;
- provider mutation met unknown outcome;
- reconciliation gestart en resultaat;
- startup recovery / scheduler rebuild;
- discrepancy / `AttentionRequired`;
- automatische Visit-finalization.

## Voorlopige eventcontext

Een event zal waarschijnlijk minimaal bevatten:

- `VisitId`;
- timestamp;
- eventtype;
- korte reden/beschrijving;
- `VisitSchedulerWorkId` indien relevant;
- `ProviderOperationId` indien relevant;
- `ProviderParkingActionId` en eventueel externe provideraction-id indien relevant;
- oude/nieuwe Visitstatus of health indien relevant;
- oude/nieuwe work-/providerstate indien relevant;
- compacte technische metadata die voor reconstructie nodig is.

Exacte velden en normalisatie worden pas vastgesteld nadat de eventcatalogus definitief is.

## Voorbeeld van gewenst resultaat

```text
12:00:00  Visit gestart
12:00:01  Provideraction active
15:55:00  Continuation work geclaimd
15:55:01  Successor gepland voor 16:00:01
15:55:02  Provideraction successor scheduled
16:00:01  Successor active
17:30:00  DesiredEndAt gewijzigd naar 18:00
18:00:00  Terminal work geclaimd
18:00:02  Provideraction gestopt
18:00:02  Visit completed
```

De timeline moet daarbij ook uitzonderingen begrijpelijk maken, bijvoorbeeld:

```text
15:55:02  Provider start response onzeker
15:55:03  Visit health = Reconciling
15:56:00  Reconciliation gestart
15:56:01  Bestaande scheduled successor gevonden
15:56:01  Provider operation bevestigd
15:56:02  Visit health = Healthy
```

## Onduidelijkheden / open vragen

Deze punten worden bewust pas na de schedulerfixes besloten:

1. Wordt dit een eigen persistente entiteit, een uitbreiding van bestaand audit/event-model, of een projectie uit bestaande durable records?
2. Welke events zijn functioneel betekenisvol genoeg om permanent te bewaren en welke blijven alleen structured logs?
3. Welke metadata moet sterk getypeerd worden en welke mag compacte JSON/details zijn?
4. Hoe lang bewaren we scheduler audit events?
5. Wie mag de timeline zien: alleen beheerder of gedeeltelijk ook gebruiker?
6. Moeten recovery/reconciliation meerdere technische subevents tonen of één samengevat domeinevent?
7. Moeten events append-only zijn en zo ja, welke database-invariant dwingt dat af?

## Implementatiemoment

SCHED-018 wordt **als laatste schedulerverbeterpunt** opgepakt. Eerst worden de schedulerstate-machine, lifecycle, provider timing/matching, locking, recovery en testharness gestabiliseerd.

Daarna bepalen we samen per definitieve flow:

> Welke actie of state transition moet persistent worden vastgelegd om achteraf te kunnen begrijpen wat de scheduler heeft gedaan en waarom?

## Verificatiecriteria

SCHED-018 kan pas op ✅ wanneer minimaal bewezen is dat:

1. iedere belangrijke schedulerflow een begrijpelijke Visit-timeline oplevert;
2. retries/recovery/reconciliation achteraf causaal te reconstrueren zijn;
3. events niet dubbel ontstaan door replay/idempotency;
4. de audit trail restartbestendig is;
5. beheer de historie per Visit kan raadplegen;
6. de audit trail geen secrets of onnodige gevoelige data bevat.
