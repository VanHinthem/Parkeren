# SCHED-009 + SCHED-010 — provider unknown/recovery en deploymentmodel

**Status:** technisch ontwerp  
**Prioriteit:** hoog  
**Ontwerpdatum:** 2 oktober 2026  
**Scope:** provider timeout/unknown outcomes, crash/restart, claimed scheduler-work, startup recovery en deploymenttopologie.

## Doel

Dit ontwerp legt één recoverycontract vast voor twee samenhangende risico's:

- een provider mutation kan extern uitgevoerd zijn terwijl de applicatie geen zekere response heeft ontvangen;
- een app/container kan stoppen terwijl scheduler-work of provideroperations halverwege de lokale flow staan.

De fundamentele strategie uit de huidige implementatie blijft behouden:

```text
persist local intent
-> mutate provider
-> confirm/read back
-> bij onzekerheid: Unknown/Reconciliation
-> nooit blind dezelfde mutation opnieuw uitvoeren
```

De belangrijkste aanscherping is dat we voor V1 expliciet één deploymentmodel ondersteunen en startup recovery daarop afstemmen.

## Besluit: V1 ondersteunt exact één actieve scheduler/app-instance

De huidige productie-compose bevat één `app` service en geen replica-/clusterconfiguratie. Voor V1 maken we dit een expliciet architectuurcontract:

> Er mag op enig moment maximaal één actieve `Parkeren.Api` instance bestaan die `VisitSchedulerWorker` uitvoert.

Dit betekent:

- één productiecontainer voor `app`;
- geen `docker compose --scale app=N` voor V1;
- geen rolling deployment waarbij oude en nieuwe app-instance tegelijk scheduler-work verwerken;
- restart/upgrade gebeurt als vervanging van de bestaande instance, niet als overlappende scheduler deployment.

De databaseprimitives (`FOR UPDATE SKIP LOCKED`, `ClaimedBy`) blijven nuttig voor concurrency binnen de applicatie en toekomstige uitbreiding, maar vormen in V1 **geen garantie voor multi-instance scheduler support**.

### Waarom deze keuze

De huidige startup recovery zet alle `Claimed` scheduler-work terug naar `Pending`. Zonder owner-liveness of lease-expiry kan een nieuw opstartende instance niet onderscheiden of een claim:

- werkelijk door een gecrashte vorige instance is achtergelaten; of
- nog actief door een andere gezonde instance wordt verwerkt.

Bij exact één actieve instance is die ambiguïteit er niet: iedere claim die bij startup nog bestaat is per definitie achtergelaten state van de vorige procesinstantie.

## Geen scheduler lease/heartbeat in V1

Voor V1 introduceren we daarom **geen** extra distributed lease- of heartbeatlaag.

Een multi-instance ontwerp zou minimaal nodig hebben:

- persistente worker/instance identity;
- claim lease-expiry;
- eventueel heartbeat/liveness;
- takeover alleen na aantoonbare lease-expiry;
- deploymentstrategie die overlappende instances ondersteunt;
- aanvullende concurrencytests over meerdere processen.

Dat is bewust buiten V1-scope. Zodra horizontale schaal of zero-downtime rolling deployment gewenst wordt, moet SCHED-010 opnieuw worden geopend voordat dat operationeel wordt toegestaan.

## Startup recovery gate blijft verplicht

`VisitSchedulerWorker` mag pas nieuwe due work claimen nadat startup recovery succesvol is afgerond.

Gewenste volgorde:

```text
app start
-> scheduler nog NIET claimen
-> achtergelaten claimed work herstellen
-> unresolved provideroperations classificeren/reconciliëren
-> Visits/scheduler-work reconstrueren
-> pas daarna scheduler claim-loop starten
```

Wanneer startup recovery faalt:

- geen nieuwe scheduler mutation uitvoeren;
- recovery opnieuw proberen;
- providerstate niet gokken;
- applicatie mag voor overige niet-mutatieve functies eventueel bereikbaar blijven, maar de scheduler blijft gated.

## Herstellen van `Claimed` scheduler-work

### V1-invariant

Omdat er exact één actieve scheduler-instance is, mag startup recovery alle persistente `Claimed` work als abandoned beschouwen.

Herstel gebeurt echter onder de lock-order uit SCHED-014:

```text
per Visit:
1. Visit advisory lock
2. scheduler work row lock
3. work opnieuw valideren
4. work-type policy uit SCHED-015 toepassen
5. Release / Cancel / direct due laten zijn
```

Niet langer als generieke bulk-update zonder Visit-semantiek.

### Policy bij recovery

De state/healthmatrix uit SCHED-015 blijft leidend:

- `ContinueProviderCoverage + Active/Healthy` -> terug naar Pending;
- `ContinueProviderCoverage + Reconciling/AttentionRequired` -> Pending maar deferred;
- `ContinueProviderCoverage + Stopping/terminal` -> Cancel;
- `StopVisit + Active/Stopping` -> Pending/direct due;
- `StopVisit + Completed/Cancelled` -> Cancel/no-op;
- `LongVisitWarning + Active` -> Pending/defer conform policy;
- terminal Visit -> Cancel.

Recovery mag dus niet simpelweg ieder Claimed item identiek releasen.

## Provider mutation state is leidend vóór scheduler retry

Een achtergelaten schedulerclaim zegt niets over de vraag of de externe provider mutation wel of niet is uitgevoerd.

Daarom geldt voor ieder workitem dat provider mutation kan veroorzaken:

1. laad gerelateerde `ProviderOperation`;
2. inspecteer operationstatus;
3. reconcile unresolved mutation vóór een nieuwe mutation mogelijk wordt;
4. pas na definitieve provideruitkomst mag scheduler-work verder.

### `Pending`

De mutation is lokaal voorbereid maar nog niet als poging begonnen. Alleen wanneer de mutation guard opnieuw bevestigt dat de intent nog geldig is, mag uitvoering plaatsvinden.

### `InProgress`

Een recent `InProgress` attempt wordt niet direct overgenomen. De bestaande attempt lease blijft als proces-crashdetectie bruikbaar.

Na startup is de vorige instance echter weg. Een achtergelaten `InProgress` mutation moet daarom uiteindelijk naar `Unknown`/reconciliation worden gebracht vóór heruitvoering, tenzij er lokaal sluitend bewijs is dat geen providercall heeft plaatsgevonden.

### `Unknown` / `Reconciling`

Geen nieuwe provider mutation.

Eerst read-back via het provider-matchingcontract uit `provider-timing-contract-technical-design.md`:

- action-id primair wanneer bekend;
- unieke tolerante fallback-match wanneer action-id onbekend is;
- `scheduled` en `active` beide semantisch geldige startuitkomsten afhankelijk van geplande tijd;
- geen exacte timestamp-equality;
- geen unieke uitkomst -> onzekerheid behouden / AttentionRequired, nooit blind retry.

### `Succeeded`

Mutation niet opnieuw uitvoeren. Scheduler/recovery reconstrueert alleen de volgende lokale intent die nog ontbreekt.

### `Failed`

Alleen definitieve providerfailure. Verdere scheduleractie wordt bepaald door het worktype en de functionele flow; geen automatische interpretatie als Unknown.

## Operation idempotency blijft de primaire duplicate-barrière

Iedere provider mutation behoudt een persistent operation/request id voordat de externe call plaatsvindt.

Bij replay/restart geldt:

- dezelfde logische scheduleractie gebruikt dezelfde bestaande operation waar die al bestaat;
- recovery maakt geen nieuwe provideroperation alleen omdat de worker opnieuw is gestart;
- bestaande `Succeeded`, `Unknown` of `Reconciling` operations worden gerespecteerd;
- een duplicate provider start/stop mag niet ontstaan door alleen een procesrestart.

## Scheduled successor na restart

Het provider-timingcontract uit SCHED-001/002/017 is expliciet onderdeel van recovery.

Voor een lokaal bekende `Scheduled` successor:

```text
vóór PlannedStartAt:
  provider read-back bevestigt scheduled
  -> lokaal Scheduled behouden
  -> geen duplicate StartNewAction

rond/na PlannedStartAt:
  remote active
  -> lokaal Scheduled -> Active
  -> predecessor zo nodig Completed
  -> volgende continuation intent reconstrueren

onzekere/ontbrekende remote state:
  -> reconciliation/AttentionRequired
  -> geen tweede successor starten
```

Een startup mag dus nooit een bestaande scheduled successor negeren en opnieuw dezelfde dekking plannen.

## Terminal Visit recovery

SCHED-013 voegt durable terminal `StopVisit` work toe. Startup recovery moet ook dit herstellen.

Voor iedere niet-terminale Visit:

1. bereken opnieuw de effectieve terminal boundary via dezelfde centrale calculator;
2. controleer of relevant terminal work bestaat;
3. ontbrekend terminal work idempotent reconstrueren;
4. als boundary tijdens downtime is verstreken, terminal work direct due maken;
5. terminal finalization semantisch vóór continuation laten winnen.

Een restart mag dus nooit leiden tot een Visit die na zijn harde eindgrens actief blijft omdat alleen continuation-work werd gerebuild.

## Recoveryvolgorde per Visit

De gewenste volgorde wordt:

```text
1. Visit advisory lock nemen voor lokale recoverymutaties
2. unresolved ProviderOperations inventariseren
3. provider reconciliation uitvoeren waar nodig
4. lokale ProviderParkingAction states bijwerken
5. Visit health/lifecycle opnieuw classificeren
6. terminal work uit SCHED-013 verzekeren
7. bestaand scheduled/active providercoverage inventariseren
8. continuation work alleen reconstrueren wanneer state/health policy dit toestaat
9. LongVisitWarning work zo nodig reconstrueren
```

Provider-I/O zelf wordt niet uitgevoerd terwijl een langdurige database rowlock wordt vastgehouden. Waar provider read-back nodig is, wordt de bestaande prepare/read/revalidate-structuur gebruikt: lokale intent/state lezen, externe read-back, daarna onder Visit-lock persistente state opnieuw valideren en toepassen.

## Ambigue recovery

Wanneer recovery niet eenduidig kan bewijzen wat provider-side gebeurd is:

- geen nieuwe provider mutation;
- Visit health naar `AttentionRequired` of bestaande reconciliationstate behouden;
- terminal `StopVisit` blijft volgens SCHED-015 wél uitvoerbaar zodat providerdekking niet onbeperkt doorloopt;
- continuation blijft durable maar deferred;
- beheerder/notificatie kan de afwijking zichtbaar maken;
- SCHED-018 zal later de relevante recoverybesluiten persistent loggen.

`AttentionRequired` betekent dus "geen nieuwe dekking gokken", niet "scheduler volledig uitschakelen".

## Relatie met SCHED-014

Ook recovery houdt de uniforme lock-order aan:

```text
Visit advisory lock
-> daarna scheduler/provider/local row locks
```

Geen recoverypad mag scheduler-work eerst `FOR UPDATE` locken en daarna op de Visit advisory lock wachten.

## Relatie met SCHED-015

Recovery gebruikt dezelfde `VisitSchedulerWorkExecutionPolicy` als normale claim/retry.

Er ontstaat geen aparte herstelmatrix die later van runtimegedrag kan afwijken.

## Relatie met SCHED-017

De kwaliteit van recovery hangt direct af van provider matching.

Daarom wordt geen SCHED-009 implementatie afgerond voordat:

- één centrale `ProviderActionMatchPolicy` bestaat;
- timestamp tolerance centraal wordt toegepast;
- scheduled future starts correct worden herkend;
- unique fallback matching na unknown bewezen is.

## Deploymentbeveiliging

Omdat single-instance een correctness-aanname is, moet die niet alleen in documentatie leven.

Voor V1 minimaal:

- deploymentdocumentatie vermeldt expliciet `app replicas = 1`;
- geen configuratie/documentatie die horizontaal schalen als ondersteund presenteert;
- bij toekomstige introductie van orchestration/replicas moet een architecture check SCHED-010 opnieuw openen.

Een runtime distributed singleton lock voor de complete scheduler kan later extra bescherming bieden, maar is niet noodzakelijk voor V1 zolang deployment daadwerkelijk single-instance blijft. We introduceren die daarom nu niet zonder concrete noodzaak.

## Teststrategie

Minimaal bewijzen:

1. restart met Claimed continuation -> policy-correct Pending/Defer en daarna één uitvoering;
2. restart met Claimed StopVisit -> terminal intent blijft bestaan en wordt hervat;
3. restart met Claimed warning -> geen verloren of dubbel warning-work;
4. restart met `InProgress` provider mutation -> geen blind duplicate request;
5. restart met `Unknown` mutation -> eerst reconciliation;
6. successful reconciliation -> scheduler wordt precies één keer herbouwd;
7. ambigu reconciliation -> AttentionRequired, geen nieuwe continuation mutation;
8. bestaande scheduled successor vóór start -> geen duplicate successor;
9. scheduled successor na start -> correcte scheduled->active recovery;
10. restart na terminal boundary -> Visit wordt alsnog via Stop/finalization afgerond;
11. recovery gebruikt lock-order Visit -> row locks;
12. startup claim-loop start niet voordat recovery succesvol is afgerond.

Multi-instance tests zijn voor V1 niet vereist omdat dit expliciet geen ondersteund deploymentmodel is.

## Voorgestelde implementatievolgorde

Na goedkeuring van het complete schedulerontwerp:

1. single-instance deploymentcontract in deploy/technische documentatie vastleggen;
2. startup claimed-work recovery via SCHED-015 policy en SCHED-014 lock-order laten lopen;
3. provider recovery aansluiten op centrale ProviderActionMatchPolicy;
4. scheduled successor recovery conform timingcontract implementeren;
5. SCHED-013 terminal work meenemen in scheduler rebuild;
6. deterministic restart/recovery integratietests toevoegen;
7. bestaande recoverytests regressie draaien.

## Besloten antwoorden op SCHED-010 open vragen

1. **Is V1 single-instance?**  
   Ja. V1 ondersteunt exact één actieve `Parkeren.Api`/scheduler-instance.

2. **Ondersteunen we nu horizontale schaal of overlappende rolling deployment?**  
   Nee. Dat is expliciet niet ondersteund zolang er geen distributed claim lease/livenessmodel bestaat.

3. **Voegen we nu claim lease/heartbeat toe?**  
   Nee. Niet nodig voor het gekozen V1 deploymentmodel. Dit wordt verplicht herontwerpwerk zodra multi-instance gewenst wordt.

## Nog open vóór implementatie

De open providerdetails uit het timingcontract blijven gelden:

- concrete timestamp tolerance;
- activation grace;
- feitelijk post-end 2Park status/visibility;
- capaciteitseffect van scheduled actions.

Deze punten beïnvloeden provider-reconciliation, maar veranderen het deployment- en recoverycontract hierboven niet.
