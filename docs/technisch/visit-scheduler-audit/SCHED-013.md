# SCHED-013 — natuurlijke Visit-afronding

**Status:** 🛠 Fix gepland  
**Prioriteit:** kritiek/hoog  
**Raakt:** normale finite Visits, free-only Visits, SCHED-007, SCHED-008, SCHED-012 en provider-reconciliation.  
**Ontwerpbesluit:** 2 oktober 2026

## Samenvatting

De audit bevestigde dat de scheduler meerdere paden heeft die correct bepalen dat **geen verdere providerdekking meer nodig is**, maar dat daarmee de logische `Visit` nog niet wordt beëindigd.

Voor V1 leggen we daarom expliciet vast dat **providerdekking** en **Visit-finalization** twee aparte verantwoordelijkheden zijn.

Een Visit krijgt een functionele, terminale grens. Zodra die grens wordt bereikt, moet durable terminal scheduler-work de normale Stop/finalization-orchestration uitvoeren, ongeacht of op dat moment nog providerdekking bestaat.

## Functionele terminale grens

Voor een Visit wordt de eerst bereikbare harde eindgrens bepaald uit:

1. `DesiredEndAt`, wanneer die niet `null` is;
2. `Visit.StartAt + MaxVisitElapsedDuration`, wanneer die limiet bestaat;
3. het tijdstip waarop `MaxPaidParkingDuration` is opgebruikt, wanneer die limiet bestaat.

De vroegste toepasselijke grens is de **effectieve terminale Visitgrens**.

`MaxPaidParkingDuration` is daarmee nadrukkelijk een harde grens van de hele Visit. Dit volgt ook uit de bestaande functionele definitie: een open-ended Visit loopt totdat hij expliciet wordt gestopt of een toepasselijke harde limiet wordt bereikt.

Een volledig open-ended Visit zonder één van deze harde grenzen heeft geen vooraf bekende terminale grens en krijgt daarom geen vooraf gepland terminal work.

## Gewenste lifecycle

```text
Starting
  -> Active
      -> Stopping
          -> Completed
```

De overgang `Active -> Stopping -> Completed` wordt gebruikt voor zowel:

- handmatig stoppen;
- bereiken van `DesiredEndAt`;
- bereiken van `MaxVisitElapsedDuration`;
- bereiken van `MaxPaidParkingDuration`.

Er komt functioneel dus geen aparte tweede lifecycle voor een “automatisch afgelopen” Visit. Het verschil zit in de **trigger/eindreden**, niet in de state transition.

## Eindreden

De technische implementatie moet de aanleiding voor het einde kunnen onderscheiden. Minimaal zijn de volgende semantieken nodig:

- `ManualStop`;
- `DesiredEndReached`;
- `MaxVisitElapsedDurationReached`;
- `MaxPaidParkingDurationReached`.

Dit hoeft niet noodzakelijk een nieuw domeinenum te worden; dat is een implementatiebesluit. De eindreden moet echter wel herleidbaar zijn voor audit, logging, recovery en toekomstige beheerdiagnostiek.

## Durable terminal work

### Finite Visit

Wanneer de effectieve terminale grens bekend is, moet er **altijd durable terminal scheduler-work** bestaan op die grens.

Dat geldt ook wanneer:

- de Visit volledig in gratis tijd valt;
- de laatste provideraction al exact op die grens eindigt;
- er na de laatste betaalde periode een gratis staart zit;
- geen provideraction ooit nodig is geweest.

Hiermee wordt Visit-finalization niet meer toevallig afhankelijk van continuation-work.

### Open-ended Visit

Bij:

```text
DesiredEndAt = null
MaxVisitElapsedDuration = null
MaxPaidParkingDuration = null
```

is er geen vooraf bekende terminale grens. De Visit blijft actief totdat een gebruiker/beheerder hem stopt of een later gewijzigde policy/Visit-state een concrete grens introduceert.

## Orchestration op de terminale grens

Op de terminale grens moet de bestaande duurzame Stop/finalization-semantiek worden hergebruikt:

```text
terminal work due
-> Visit opnieuw valideren
-> Visit naar Stopping
-> alle nog open provideractions veilig afhandelen
-> unresolved provider-mutaties reconciliëren indien nodig
-> pas wanneer geen provideraction meer openstaat:
   Visit -> Completed
```

Belangrijk: een provideraction die vanzelf op dezelfde grens is afgelopen hoeft niet kunstmatig opnieuw gestopt te worden. De lokale provideraction-state moet dan eerst betrouwbaar als terminal (`Completed`/equivalent) worden bevestigd of gereconcilieerd.

Een scheduled opvolger die de Visitgrens overschrijdt mag de Visit nooit overleven en moet vóór finalization worden geannuleerd.

## `ActualEndAt`

Voor een geplande natuurlijke eindgrens gebruiken we als functionele `Visit.ActualEndAt` de **effectieve terminale Visitgrens**.

Provider read-back timestamps bepalen niet de Visit-eindtijd. Echte 2Park-tests hebben al aangetoond dat provider timestamps enkele seconden kunnen afwijken. Provider timestamps blijven bewijs voor provideraction-state en reconciliation, maar veranderen niet achteraf de functionele Visitgrens.

Bij handmatig stoppen blijft `ActualEndAt` het werkelijke stopmoment van de Stop-flow.

## Relatie met providerdekking

Providerdekking mag vóór de terminale Visitgrens uit meerdere actions bestaan en gratis intervallen overslaan.

De volgende invariant geldt:

> `geen verdere providerdekking nodig` is nooit voldoende om een Visit lokaal actief te laten zonder een eigen terminale finalization-route.

Omgekeerd geldt:

> Visit-finalization mag nooit plaatsvinden zolang een actieve/scheduled/unknown provideraction nog providerwerk kan vereisen.

## Relatie met continuation

Continuation-work blijft verantwoordelijk voor providerdekking tot maximaal de terminale Visitgrens.

Wanneer continuation berekent dat een harde grens is bereikt:

- mag het geen nieuwe providerdekking meer starten;
- hoeft het niet zelf de Visit te completeren;
- het terminale scheduler-work is verantwoordelijk voor lifecycle-finalization.

Hierdoor hebben continuation en Visit-finalization ieder één duidelijke verantwoordelijkheid.

## `DesiredEndAt` wijzigen

Bij wijzigen van `DesiredEndAt` moet de effectieve terminale grens opnieuw worden berekend.

### Verkorten

- obsolete terminal work wordt durable geannuleerd/vervangen;
- nieuw terminal work komt op de nieuwe effectieve grens;
- bestaand continuation-work na die grens wordt geannuleerd;
- provideractions die de nieuwe grens overschrijden worden volgens de bestaande durable cancel/stop-semantiek afgehandeld.

### Verlengen

- alleen wanneer de nieuwe `DesiredEndAt` daadwerkelijk de vroegste grens was, verschuift terminal work;
- een kortere `MaxVisitElapsedDuration`- of `MaxPaidParkingDuration`-grens blijft leidend;
- continuation-work wordt alleen toegevoegd voor de extra betaalde dekking vóór de nieuwe effectieve terminale grens.

## Restart/recovery

Startup recovery moet naast ontbrekend continuation-work ook ontbrekend terminal work kunnen reconstrueren.

Voor iedere `Active` Visit met een berekenbare terminale grens geldt na recovery:

```text
exact één relevante Pending/Claimed terminale taak
```

Als de terminale grens tijdens downtime al verstreken is, moet het work direct due zijn en de Visit via dezelfde Stop/finalization-route afhandelen.

## Scheduler-prioriteit op gelijke timestamp

Wanneer terminal work en ander work exact dezelfde `DueAt` hebben, moet terminal work semantisch winnen van work dat de Visit wil voortzetten of alleen een notificatie produceert.

De concrete claim-ordering hoort bij SCHED-015/SCHED-014, maar de functionele regel ligt hier vast:

> Op of na de terminale Visitgrens mag geen nieuwe continuation of Long Visit reminder meer ontstaan.

## Bestaande code die hergebruikt kan worden

De huidige implementatie heeft al belangrijke bouwstenen:

- `StopVisitFlow`;
- durable `ProviderOperation` voor Stop;
- `StopVisitProviderExecutor` en reconciliation;
- `StopVisitFinalizer` met controle dat geen open provideraction resteert;
- cancellation van pending scheduler-work tijdens Stop;
- end-time-adjustment voor actieve/scheduled provideractions.

De oplossing hoeft daarom geen tweede finalization-mechanisme te introduceren. De ontbrekende laag is vooral het **durable plannen en herstellen van het natuurlijke einde**.

## Besloten antwoorden op eerdere open vragen

1. **Eén generieke eindflow?**  
   Ja. Automatische natuurlijke eindes hergebruiken dezelfde Stop/finalization-orchestration; trigger/eindreden verschilt.

2. **Altijd terminal work voor finite Visit?**  
   Ja. Zodra een effectieve terminale grens berekenbaar is, moet durable terminal work bestaan, ook zonder provideraction.

3. **Welke `ActualEndAt` bij providerafwijkingen?**  
   Bij natuurlijk einde: de functionele effectieve terminale Visitgrens. Provider timestamps veranderen die niet.

4. **Wat doet `MaxPaidParkingDuration`?**  
   Het is een harde Visitgrens en beëindigt de hele Visit wanneer de maximaal toegestane betaalde tijd is opgebruikt.

## Nog open voor technische uitwerking

1. Houden we `VisitSchedulerWorkType.StopVisit` als naam voor zowel handmatig/deferred als natuurlijk terminal work, of introduceren we een semantisch duidelijkere `EndVisit` worktype?
2. Waar leggen we de eindreden persistent vast: op Visit, scheduler-work, root `ProviderOperation` of audit-event?
3. Welke helper wordt de enige bron voor het berekenen van de effectieve terminale grens, zodat start, end-time change, scheduler en recovery exact dezelfde logica gebruiken?
4. Hoe modelleren we de wall-clock grens voor `MaxPaidParkingDuration` efficiënt over versioned rulesets heen zonder verschillende implementaties op meerdere plekken?

Deze punten zijn technische ontwerpkeuzes; de functionele semantiek hierboven staat vast.

## Verificatiecriteria na implementatie

SCHED-013 kan pas naar ✅ wanneer tests minimaal bewijzen:

1. finite paid Visit eindigt automatisch op de effectieve grens;
2. volledig gratis Visit eindigt automatisch zonder provideraction;
3. paid → free tail eindigt op de Visitgrens en niet bij einde providerdekking;
4. overnight Visit blijft tijdens gratis interval actief en eindigt pas op zijn Visitgrens;
5. `MaxVisitElapsedDuration` beëindigt de Visit;
6. `MaxPaidParkingDuration` beëindigt de Visit op de berekende betaald-tijdboundary;
7. handmatige Stop vóór terminal work wint en terminal work daarna geen effect meer heeft;
8. end-time verkorten/verlenging vervangt terminal work correct;
9. startup recovery herbouwt ontbrekend terminal work;
10. downtime voorbij de eindgrens resulteert na restart alsnog in finalization;
11. geen provideraction/schedulerwork de afgeronde Visit functioneel overleeft;
12. capaciteit direct vrijkomt zodra de Visit `Completed` is.