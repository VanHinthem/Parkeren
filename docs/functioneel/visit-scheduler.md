# Visit scheduler — functioneel proces

Dit document beschrijft de actuele V1-semantiek van de Visit scheduler na de scheduler-hardening van oktober 2026.

## Doel

De scheduler voert tijdgestuurde werkzaamheden duurzaam uit. Hij is geen bron van parkeerbeleid.

De functionele bron blijft:

- de Visit en zijn policy snapshot;
- het providerproduct dat bij start is vastgelegd;
- de geldige versioned `ParkingRuleSet`.

## Scheduler work-types

V1 kent drie typen persistent schedulerwork:

| Type | Functioneel doel |
| --- | --- |
| `ContinueProviderCoverage` | Providerdekking verzorgen vóór een volgend betaald segment. |
| `StopVisit` | De Visit op een functionele terminale grens via de normale Stop/finalization-flow afhandelen. |
| `LongVisitWarning` | Een langdurige actieve Visit signaleren en eventueel reminders plannen. |

## Work-type policy

Schedulerwork wordt niet meer generiek met `Active + Healthy` beoordeeld.

### ContinueProviderCoverage

- `Active + Healthy` → uitvoeren;
- `Active + Reconciling/AttentionRequired/StopFailed` → uitstellen, niet definitief verwijderen;
- `Starting` → uitstellen;
- `Stopping`, `Completed`, `Cancelled` → annuleren.

### StopVisit

Terminal Stop-work mag niet door technische health worden geblokkeerd. Voor `Starting`, `Active` en `Stopping` kan het uitvoerbaar blijven; alleen definitief terminale Visits annuleren het work.

### LongVisitWarning

Een actieve Visit kan een warning krijgen ongeacht health. `Starting` wordt uitgesteld; `Stopping` en terminale lifecycle-states annuleren de warning.

## Terminale Visitgrens

Providerdekking en Visit-finalization zijn gescheiden verantwoordelijkheden.

De vroegste toepasselijke grens uit:

1. `DesiredEndAt`;
2. `StartAt + MaxVisitElapsedDuration`;
3. het moment waarop `MaxPaidParkingDuration` is verbruikt;

wordt als duurzame `StopVisit`-taak gepland.

De eindreden wordt persistent vastgelegd als `DesiredEndReached`, `MaxVisitElapsedDurationReached` of `MaxPaidParkingDurationReached`. Een handmatige stop gebruikt `ManualStop`.

Bij automatische finalization is `ActualEndAt` de functionele schedulerboundary, ook wanneer de worker later uitvoert.

## Start en aaneengesloten continuation

Voor Oss gebruikt de scheduler `StartNewAction`.

Bij aaneengesloten betaalde dekking:

```text
precheck = predecessor.End - 5 minuten
successor.Start = predecessor.End + 1 seconde
```

Op T-5 mag exact één toekomstige successor worden aangemaakt. Die provideraction is vóór zijn start `scheduled` en wordt vanaf zijn Start dynamisch als `active` gelezen.

Redundant schedulerwork, replay of restart mag geen tweede successor maken.

## Gratis periode / overnight

Tijdens gratis tijd bestaat geen providerdekking.

Als later opnieuw betaald parkeren begint:

```text
precheck        = nextPaid.Start - 5 minuten
successor.Start = nextPaid.Start
```

De Visit blijft tijdens het gratis interval logisch actief. Er wordt geen `+1 seconde` toegepast over een echt gratis gat.

## Open-ended Visits

Een volledig open-ended Visit zonder eerdere harde grens gebruikt een rolling planninghorizon van 14 dagen.

De horizon:

- wordt opnieuw vooruitgeschoven zolang de Visit actief blijft;
- maakt geen `DesiredEndAt` of `ActualEndAt`;
- veroorzaakt geen terminal `StopVisit`;
- is geen maximale Visitduur.

## Eindtijd verkorten

Bij verkorten:

- wordt de terminale boundary opnieuw berekend;
- obsolete pending terminal-work wordt vervangen;
- continuation voorbij de nieuwe grens wordt geannuleerd;
- scheduled successors die niet meer passen worden duurzaam geannuleerd/vervangen;
- providerdekking voorbij de grens wordt via Stop afgehandeld.

## Eindtijd verlengen

Bij verlengen:

- wordt de effectieve terminale boundary opnieuw berekend;
- een eerdere elapsed- of paid-durationgrens blijft leidend;
- alleen extra benodigde betaalde providerdekking wordt ingepland;
- continuation blijft JIT en bouwt niet onbeperkt een keten vooruit.

## Handmatig stoppen

Manual Stop blijft leidend bij races met continuation.

De Stop-flow serialiseert op de Visit, zet de Visit naar `Stopping`, annuleert niet meer relevante scheduleritems en handelt alle nog open actieve én scheduled provideractions af voordat de Visit `Completed` wordt.

## Provider matching en uncertain outcomes

Provideridentificatie gebruikt centraal:

1. bekende provider action-id;
2. productcontext;
3. genormaliseerd kenteken indien relevant;
4. semantisch geldige status;
5. Start/End binnen 5 seconden indien timestamps voor die match relevant zijn.

De 5 seconden zijn een **engineering margin**, geen gemeten 2Park-SLA.

Zonder bekende action-id is alleen één unieke fallbackkandidaat toegestaan. Een bekende action-id mismatch valt nooit terug naar een andere action.

Een timeout of onzekere providerresponse leidt tot read-back/reconciliation vóór een mutation opnieuw mag worden uitgevoerd.

## Discrepancy

Een echte externe afwijking kan een Visit naar `AttentionRequired` brengen. Continuation wordt dan uitgesteld/geblokkeerd. Terminal Stop-work blijft beschikbaar wanneer de Visit functioneel moet eindigen.

End-drift binnen 5 seconden geldt niet als discrepancy; drift buiten de centrale tolerance kan dat wel zijn.

## Recovery en deployment

V1 heeft exact één actieve scheduler-instance. Rolling deployment met twee tegelijk actieve workers of horizontale schaal hoort niet bij het huidige contract.

Bij startup:

1. recovery draait vóór de claimloop;
2. achtergelaten claimed work wordt policygedreven hersteld;
3. stale `InProgress` provideroperations worden pas na de bestaande 5-minuten attempt lease naar `Unknown` gebracht;
4. onderbroken `Reconciling` operations worden via read-back hervat;
5. continuation- en terminal-work worden veilig herbouwd.

## Long Visit warning

Een actieve Visit kan volgens de ingestelde `LongVisitWarningAfter` een warning krijgen. Een technisch ongezonde healthstatus verwijdert die warning niet meer automatisch. Een reminder wordt alleen gepland wanneer een reminderinterval is geconfigureerd.

## Nog bewust extern/onbewezen

Twee providerdetails zijn nog niet als live 2Park-contract vastgelegd:

- exacte status/zichtbaarheid nadat een provideraction natuurlijk over `End` heen is;
- of een toekomstige `scheduled` action meetelt voor provider-capaciteit.

TwoParkMock kan beide gedragingen expliciet configureren voor tests, maar kiest bewust geen onbewezen providerdefault.

## Betrouwbaarheidsstatus

De scheduler-hardening voor SCHED-001 t/m SCHED-017 is geïmplementeerd en scenario-voor-scenario opnieuw geverifieerd. Persistente scheduler-observability (`SCHED-018`) is nog niet gestart en is een aparte vervolgstap.
