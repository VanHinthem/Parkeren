# SCHED-014 — uniforme lock-order en schedulerclaim

**Status:** technisch ontwerp  
**Prioriteit:** hoog  
**Ontwerpdatum:** 2 oktober 2026  
**Raakt:** schedulerclaim, retry/release, Stop Visit, end-time changes, recovery en toekomstige Visit-mutaties.

## Doel

Alle transacties die zowel Visit-state als schedulerwork kunnen raken gebruiken één vaste lock-order. Daarmee verdwijnt de huidige deadlockcyclus waarin de scheduler eerst een work-row lock neemt en daarna op de Visit advisory lock wacht, terwijl Stop/end-time mutaties juist de omgekeerde volgorde gebruiken.

## Besluit: Visit-lock altijd eerst

De globale regel wordt:

```text
1. pg_advisory_xact_lock(VisitId)
2. Visit-state opnieuw lezen
3. scheduler/provider rows locken of wijzigen
4. transactionele wijziging afronden
```

Geen codepad mag een `VisitSchedulerWork` row-lock vasthouden terwijl nog op de Visit advisory lock wordt gewacht.

Deze regel sluit aan bij de bestaande Stop-, end-time- en recoveryflows, die de Visit advisory lock al als serializeerpunt gebruiken.

## Schedulerclaim wordt tweefasenclaim

De scheduler kent bij het zoeken naar het eerstvolgende due work-item vooraf nog geen VisitId. Daarom wordt kandidaatselectie losgekoppeld van de daadwerkelijke claim.

### Fase 1 — kandidaat identificeren zonder row lock

Selecteer een kleine geordende set due `Pending` work-items:

```text
ORDER BY DueAt, work-type priority, CreatedAt
```

Hierbij wordt nog geen `FOR UPDATE` gehouden.

De selectie is uitsluitend een kandidaatlijst. Er mag uit deze read geen eigenaarschap worden afgeleid.

### Fase 2 — Visit serialiseren en work revalideren

Per kandidaat:

```text
begin transaction
-> pg_advisory_xact_lock(candidate.VisitId)
-> SELECT exact work row FOR UPDATE
-> opnieuw controleren:
   - row bestaat nog
   - Status == Pending
   - DueAt <= now
   - Visit-state/health opnieuw lezen
   - VisitSchedulerWorkExecutionPolicy evalueren
-> Execute: Claim
-> Defer: nieuwe DueAt
-> Cancel: Cancel
-> commit
```

Als de kandidaat tijdens het wachten al door een andere actor is veranderd, wordt hij overgeslagen en probeert de claimer de volgende kandidaat.

Hierdoor blijft claimveiligheid behouden zonder de verboden richting `work-row -> Visit-lock`.

## Waarom geen row lock tijdens kandidaatselectie

De kandidaatread is bewust optimistisch.

Racevoorbeeld:

```text
worker A leest work X als kandidaat
worker B leest work X ook als kandidaat
worker A krijgt Visit-lock
worker A lockt X en claimt X
worker A commit
worker B krijgt daarna Visit-lock
worker B leest X opnieuw FOR UPDATE
worker B ziet Status == Claimed
worker B slaat X over
```

Er ontstaat geen duplicate claim. De Visit advisory lock serializeert concurrerende beslissingen voor dezelfde Visit en de row revalidation voorkomt stale kandidaatdata.

## `FOR UPDATE SKIP LOCKED`

`SKIP LOCKED` blijft nuttig voor de tweede fase wanneer een row om een andere reden tijdelijk vergrendeld is, maar het mag niet meer vóór de Visit-lock worden gebruikt als langdurig vastgehouden claimlock.

Wanneer de exacte kandidaatrow na het verkrijgen van de Visit-lock niet direct bruikbaar is, wordt die kandidaat overgeslagen. De scheduler probeert daarna ander due work.

## Work-prioriteit bij gelijke `DueAt`

In combinatie met SCHED-013 en SCHED-015 krijgt de kandidaatselectie een expliciete prioriteit.

Voor gelijke `DueAt`:

```text
StopVisit
ContinueProviderCoverage
LongVisitWarning
```

Reden:

- terminal finalization moet continuation winnen;
- continuation is functioneel belangrijker dan een waarschuwing;
- een warning mag nooit de terminale boundary vertragen.

Deze ordering vervangt geen Visit-lock/revalidation; hij reduceert alleen onnodige races tussen workitems die tegelijk due zijn.

## Meerdere workitems voor dezelfde Visit

Na het verkrijgen van de Visit-lock mag de claimer de Visit en de gekozen work-row atomair beoordelen.

Als `StopVisit` de Visit naar `Stopping` brengt, worden continuation/warning items volgens SCHED-015 definitief irrelevant en door Stop/cancellation/revalidation afgehandeld.

Er is dus geen behoefte aan een tweede scheduler-specifieke mutex naast de Visit advisory lock.

## `ReleaseFailedAsync`

Ook retry/release moet dezelfde lock-order volgen.

Nieuwe flow:

```text
1. read work metadata zonder row lock om VisitId te kennen
2. begin transaction
3. pg_advisory_xact_lock(VisitId)
4. exact work FOR UPDATE
5. controleren dat Status == Claimed en ClaimedBy == workerId
6. Visit-state lezen
7. SCHED-015 execution policy evalueren
8. Release / Defer / Cancel
9. commit
```

Het stale eerste read-resultaat is geen probleem: alle beslissingen worden na de lock opnieuw gevalideerd.

## Stop Visit en end-time changes

`PostgresStopVisitClaimer` en `PostgresVisitEndTimeChanger` gebruiken al de gewenste hoofdrichting:

```text
Visit advisory lock
-> Visit/provider/scheduler state
```

Bij implementatie worden deze flows wel opnieuw gecontroleerd op alle nested calls. Een helper of subcomponent mag niet intern alsnog eerst een scheduler-row lock nemen en daarna opnieuw een Visit-lock proberen te verkrijgen.

## Recovery

Recovery en scheduler-rebuild volgen dezelfde regel wanneer zij per Visit muteren:

```text
Visit advisory lock
-> actuele Visit/provider/scheduler state opnieuw lezen
-> work herstellen/aanmaken/wijzigen
```

Startup mag claimed work niet via een tegengestelde lock-order vrijgeven.

De vraag of claims van een andere nog levende instance mogen worden vrijgegeven blijft onderdeel van SCHED-010; deze lock-order lost die deploymentsemantiek niet zelfstandig op.

## Deadlock retry

Een consistente lock-order is de primaire oplossing. PostgreSQL-deadlocks mogen niet als normale schedulercoördinatie worden gebruikt.

Wel blijft een **beperkte infrastructuurretry voor PostgreSQL `40P01`** toegestaan als defensieve bescherming tegen andere, nog onbekende database-lockcombinaties.

Voorwaarden:

- bounded retry;
- volledige transactie opnieuw uitvoeren;
- alleen voor idempotente/durable flows;
- logging met operatie/work/Visit-id;
- geen blind retry van een provider mutation buiten de bestaande `ProviderOperation`-idempotency/reconciliation.

Een deadlockretry vervangt dus nooit de lock-orderregel.

## Claim starvation

Omdat kandidaatselectie optimistisch is, kan het eerste item soms stale of tijdelijk onbruikbaar zijn.

Om te voorkomen dat één kandidaat de workerloop domineert:

- lees een kleine batch geordende kandidaten;
- probeer kandidaten in volgorde;
- stop na de eerste succesvolle state transition (`Claim`, `Defer` of `Cancel`);
- als geen kandidaat bruikbaar is, eindigt de claim-run zonder busy loop.

De exacte batchgrootte is operationele tuning en hoeft geen domeinsetting te worden.

## Transactiegrens

Provider-I/O gebeurt nooit terwijl de Visit advisory transaction lock voor schedulerclaim wordt vastgehouden.

De claimtransactie doet uitsluitend:

- lock;
- revalidation;
- policybesluit;
- durable claim/defer/cancel.

Daarna commit. De workerprocessor voert externe providercalls buiten die claimtransactie uit via de bestaande durable ProviderOperation-patterns.

Zo blijft de locktijd kort en wordt één langzame 2Park-call geen database-serializeerpunt.

## Relatie met SCHED-013

Terminal `StopVisit` gebruikt dezelfde Visit-lock en heeft bij gelijke tijd prioriteit.

Als Stop wint:

- Visit wordt `Stopping`;
- continuation kan na revalidation niet meer worden geclaimd;
- pending irrelevante workitems worden geannuleerd/no-op volgens SCHED-015.

Als continuation net vóór terminal Stop is geclaimd, moeten de provider-mutation guards vóór een externe mutation opnieuw de actuele Visit-state/terminal boundary valideren. De lock-order alleen is dus niet de enige safetylaag.

## Relatie met SCHED-015

De locklaag beslist niet zelf welke state gezond/geldig is.

Na Visit-lock en row revalidation wordt uitsluitend `VisitSchedulerWorkExecutionPolicy` gebruikt:

```text
Execute / Defer / Cancel
```

Daarmee blijven locking en schedulersemantiek gescheiden verantwoordelijkheden.

## Deterministische concurrencytests

Minimaal bewijzen:

1. schedulerclaim versus handmatige Stop op dezelfde Visit eindigt zonder deadlock;
2. schedulerclaim versus `DesiredEndAt` change eindigt zonder deadlock;
3. `ReleaseFailedAsync` versus Stop eindigt zonder deadlock;
4. twee workers die dezelfde kandidaat zien claimen hem maximaal één keer;
5. twee verschillende Visits kunnen parallel worden geclaimd;
6. Stop en continuation op gelijke `DueAt` resulteren niet in providercontinuation na terminale intent;
7. stale kandidaat na Visit-lock wordt veilig overgeslagen;
8. Cancel/Defer uit SCHED-015 gebeurt onder dezelfde serializeerregel;
9. geforceerde PostgreSQL `40P01` buiten deze specifieke lock-order wordt bounded herprobeerd zonder providerduplicate.

Tests moeten echte PostgreSQL-transacties gebruiken; een in-memory provider/database test bewijst dit lockingcontract niet.

## Implementatievolgorde

Na akkoord op het totale schedulerontwerp:

1. query-prioriteit voor schedulerwork centraliseren;
2. `ClaimNextDueAsync` ombouwen naar kandidaatread -> Visit-lock -> row-lock/revalidate;
3. SCHED-015 policy integreren in claimbesluit;
4. `ReleaseFailedAsync` naar dezelfde lock-order brengen;
5. recoverypaden controleren en waar nodig gelijk trekken;
6. deterministic PostgreSQL race-tests toevoegen;
7. optioneel bounded `40P01` transaction retry als defensieve infrastructuurlaag toevoegen;
8. regressies uitvoeren voor handmatige Stop, end-time change en terminal work uit SCHED-013.

## Besloten antwoorden op SCHED-014

1. **Welke lock komt altijd eerst?**  
   De Visit advisory lock.

2. **Kunnen we due work selecteren zonder row lock tijdens het wachten?**  
   Ja. Kandidaatselectie is optimistisch; eigenaarschap ontstaat pas na Visit-lock, row-lock en volledige revalidation.

3. **Deadlock retry?**  
   Ja, beperkt en defensief voor database-deadlocks, maar nooit als vervanging van consistente lock-order en nooit als blind provider-retry.

## Nog open

1. Exacte kandidaat-batchgrootte en korte idle/backoff zijn operationele tuning.
2. Multi-instance claim-leases/liveness blijven SCHED-010-scope.
3. De exacte guard direct vóór provider mutation wordt samen met de implementatie van SCHED-001/013 gecontroleerd.

Deze open punten veranderen de lock-order zelf niet.