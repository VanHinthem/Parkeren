# Visit scheduler

De Visit scheduler verzorgt tijdgestuurde provideracties die nodig zijn om een logische parkeer-`Visit` correct uit te voeren. De scheduler is **geen tweede bron van parkeerbeleid**: gebruikersbeleid en `ParkingRuleSet` bepalen wat mag en nodig is; de scheduler voert de daaruit afgeleide providerhandelingen duurzaam uit.

## Functionele verantwoordelijkheid

Een `Visit` kan langer duren dan één provider-action en kan betaalde en gratis perioden bevatten. De scheduler kent twee soorten toekomstig werk:

| Work type | Functioneel doel |
| --- | --- |
| `ContinueProviderCoverage` | Zorg dat een actieve Visit tijdens een volgend betaald segment voldoende providerdekking houdt. |
| `StopVisit` | Stop een actieve provider-action op een eerdere `DesiredEndAt` wanneer de provider de action-eindtijd niet kan inkorten. |

De scheduler verandert niet zelfstandig de door de gebruiker gekozen Visitduur. Hij realiseert de vastgelegde `DesiredEndAt`, policy snapshot en parkeerregels.

## Providerdekking voortzetten

De scheduler houdt rekening met `DesiredEndAt`, `MaxVisitElapsedDuration`, `MaxPaidParkingDuration`, versioned `ParkingRuleSet`-perioden, betaalde/gratis segmenten, `MaxProviderActionDuration` en de `Continuation`-strategie.

Voor Oss is `Continuation = StartNewAction`. De scheduler maakt dus een opvolgende provider-action in plaats van de bestaande action te verlengen.

### JIT continuation

`ProviderCoverageSchedule.PrecheckAt` plant de controle vijf minuten vóór het geplande einde van de actuele provider-action. De scheduler controleert eerst de actuele providerstatus en maakt pas daarna, indien nodig, de volgende action.

Wanneer het volgende betaalde segment later begint, wordt het work-item vrijgegeven tot dat tijdstip. Er wordt geen providerdekking over een gratis periode gemaakt.

### Open-ended Visits

Een volledig open-ended Visit gebruikt een technische rolling horizon van **14 dagen** (`OpenEndedPlanningHorizon`). Dit is geen functionele maximale Visitduur. Als de Visit open blijft, wordt de horizon later opnieuw vooruitgeschoven.

## Active shortening

De huidige 2Park-interface voor Oss kan de eindtijd van een actieve action niet betrouwbaar wijzigen. Daarom:

1. wordt de eerdere `DesiredEndAt` gevalideerd en toegepast;
2. worden scheduled provider-actions die niet meer passen geannuleerd of vervangen;
3. wordt, als een actieve action voorbij de nieuwe eindtijd loopt, één persistent `StopVisit` work-item met `DueAt = DesiredEndAt` gemaakt;
4. blijft de actieve provider-action tot die tijd actief;
5. voert de scheduler op `DueAt` de normale duurzame provider-stop uit;
6. wordt na bevestigde stop de Visit afgerond en capaciteit vrijgegeven.

## Technisch model

`VisitSchedulerWork` bevat `Id`, `VisitId`, `Type`, `DueAt`, `Status`, `CreatedAt`, `ClaimedAt`, `ClaimedBy`, `CompletedAt` en `Version`.

Statusovergangen:

```text
Pending -> Claimed -> Completed
                   -> Pending     (release/retry)
Pending/Claimed    -> Cancelled
```

`Completed` en `Cancelled` zijn eindstatussen.

## Worker

`VisitSchedulerWorker` is een `BackgroundService`. Bij startup wordt **eerst Visit recovery uitgevoerd**. Zolang die recovery faalt, claimt de scheduler geen provider-mutaties.

Na succesvolle recovery:

1. worden periodieke provider/recoverycontroles uitgevoerd;
2. wordt het eerstvolgende due work-item geclaimd;
3. wordt het work-item verwerkt;
4. wordt het bij een onverwachte fout, indien nog geldig, voor retry vrijgegeven.

Zonder werk wacht de worker vijf seconden. Iedere minuut worden unknown provider operations en actieve provider-actions gereconcilieerd.

## Claiming en concurrency

`PostgresVisitSchedulerWorkClaimer` gebruikt PostgreSQL `FOR UPDATE SKIP LOCKED`. Meerdere worker-instanties kunnen daardoor concurrerend claimen zonder hetzelfde work-item tegelijk te verwerken.

Na de row lock wordt ook de transactionele advisory lock van de Visit verkregen. Dezelfde Visit-lock wordt gebruikt door stop- en end-time-mutaties. Schedulerclaims worden zo geserialiseerd met wijzigingen aan dezelfde Visit.

Voor het claimen wordt de Visit opnieuw gecontroleerd. Alleen `Active` + `Healthy` levert uitvoerbaar schedulerwerk op; anders wordt het work-item geannuleerd.

## Idempotency en provider operations

Het scheduler-work-id fungeert als stabiele operation/correlation id voor de providerhandeling.

Bij continuation wordt gecontroleerd of een eerdere `ProviderOperation` met hetzelfde operation-id al succesvol is afgerond. Bij scheduled `StopVisit` wordt `work.Id` als `StopVisitCommand.OperationId` gebruikt. Daardoor loopt de stop door dezelfde persistente `ProviderOperation`- en idempotency-flow als een normale Visit-stop.

Provider-mutaties zijn dus niet afhankelijk van het geheugen van de background worker.

## Retry en reconciliation

Een gewone technische fout kan het claimed work met een nieuwe `DueAt` vrijgeven; momenteel is de retry-delay één minuut.

Als een providercall mogelijk is uitgevoerd maar het resultaat niet betrouwbaar is bevestigd, wordt niet blind opnieuw gemuteerd. De bijbehorende `ProviderOperation` blijft unresolved/unknown en recovery/reconciliation controleert eerst de provider.

Dit voorkomt de gevaarlijke strategie “timeout = dezelfde provideractie gewoon nogmaals uitvoeren”.

## Providerstatus controleren

Voor continuation vertrouwt de scheduler niet alleen op lokale state. Relevante bestaande actions worden opnieuw bij de provider gelezen.

Als de verwachte action ontbreekt of een onverwachte status/eindtijd heeft, wordt niet blind verder gepland. De Visit wordt waar nodig `AttentionRequired` of `Reconciling` en schedulerwerk wordt gestopt of uitgesteld. Een extern gestopte action leidt dus niet automatisch tot een nieuwe action.

## Wijzigingen tijdens een actieve Visit

Stop- en end-time-flows kunnen schedulerwerk ongeldig maken. Bij een stop worden pending scheduler-items geannuleerd. Bij een end-time change wordt obsolete continuation/stop-work geannuleerd en alleen werk behouden of gemaakt dat bij de nieuwe gewenste eindtijd past.

De functionele Visit-state blijft leidend; schedulerwerk is een vervangbaar uitvoeringsplan.

## Invarianten

1. Schedulerwerk vergroot nooit zelfstandig de functionele Visitduur.
2. Een gestopte of ongezonde Visit krijgt geen nieuwe providerdekking.
3. Provider-mutaties zijn persistent gecorreleerd en replay-safe.
4. Een onzeker providerresultaat wordt gereconcilieerd vóór dezelfde mutatie opnieuw wordt geprobeerd.
5. Gratis perioden krijgen geen onnodige providerdekking.
6. Provider-action-, paid-duration- en elapsed-durationgrenzen worden gerespecteerd.
7. Een scheduled `StopVisit` gebruikt dezelfde duurzame stop/finalization-semantiek als een handmatige stop.
8. De 14-daagse open-ended horizon is alleen een technische planninghorizon en beëindigt de Visit niet.

## Relevante componenten

| Component | Verantwoordelijkheid |
| --- | --- |
| `VisitSchedulerWorker` | Background loop, startup recovery, periodieke reconciliation en foutafhandeling. |
| `PostgresVisitSchedulerWorkClaimer` | Atomair claimen/vrijgeven van due work. |
| `VisitSchedulerWorkProcessor` | Uitvoeren van continuation en scheduled stop. |
| `ProviderCoverageSchedule` | JIT precheck, rolling horizon en volgend betaald segment. |
| `ProviderContinuationStartStore` / `ContinueVisitStartExecutor` | Duurzame `StartNewAction` continuation. |
| `IProviderStopStore` / `StopVisitProviderExecutor` / `IStopVisitFinalizer` | Duurzame scheduled stop en Visit-finalization. |
| `VisitRecoveryService` | Startup en periodieke reconciliation. |
| `PostgresVisitEndTimeChanger` | Annuleert obsolete work en plant deferred stop bij active shortening. |

## Oss / 2Park

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
Continuation precheck     = T-5 minuten
Open-ended horizon        = 14 dagen
Active shortening         = durable StopVisit op DesiredEndAt
```

De maximale provider-actionduur en continuation-strategie horen bij parkeer/providerregels. De T-5 precheck en 14-daagse horizon zijn technische schedulerkeuzes.
