# Parkeerbezoeken (Visits)

Een **Visit** is één logisch parkeerbezoek. De Visit staat los van de onderliggende 2Park/provider-actions: één Visit kan uit meerdere provideractions bestaan en kan betaalde en gratis perioden bevatten.

## Lifecycle

De V1 lifecycle is:

```text
Starting -> Active -> Stopping -> Completed
            \-> Cancelled   (alleen bij veilig afgebroken start)
```

Operationele health (`Healthy`, `Reconciling`, `AttentionRequired`, `StopFailed`) staat los van de lifecycle.

## Eindredenen

Een Visit kan eindigen door:

- `ManualStop`;
- `DesiredEndReached`;
- `MaxVisitElapsedDurationReached`;
- `MaxPaidParkingDurationReached`.

Handmatig en automatisch eindigen gebruiken dezelfde duurzame Stop/finalization-flow. Voor een automatisch einde is `ActualEndAt` de functionele terminale Visitgrens; voor een handmatige stop is dit het werkelijke stopmoment.

## Effectieve terminale grens

De Visit eindigt op de vroegste toepasselijke grens uit:

1. `DesiredEndAt`;
2. `StartAt + MaxVisitElapsedDuration`;
3. de wall-clock boundary waarop `MaxPaidParkingDuration` is verbruikt.

Gratis tijd telt niet mee voor `MaxPaidParkingDuration`.

Een volledig open-ended Visit zonder deze grenzen heeft geen vooraf bekende functionele eindtijd. De technische 14-daagse schedulerhorizon is nadrukkelijk geen maximale Visitduur.

## Providerdekking

Providerdekking is een aparte verantwoordelijkheid. Een Visit kan actief blijven terwijl er tijdens gratis tijd geen provideraction bestaat.

Voor Oss geldt:

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
JIT precheck              = T-5 minuten
```

Bij aaneengesloten betaalde dekking wordt maximaal één toekomstige successor JIT aangemaakt. Live 2Park-tests hebben bevestigd dat de successor wegens overlapcontrole start op:

```text
predecessor.End + 1 seconde
```

Na een echte gratis periode begint de nieuwe providerdekking exact op het volgende betaalde beginmoment; daar wordt geen `+1 seconde` toegepast. De scheduler probeert die action al op T-5 klaar te zetten.

## Stoppen

Een gebruiker moet een eigen actieve Visit handmatig kunnen stoppen. Stop heeft voorrang op continuation.

De Stop-flow:

1. serialiseert op de Visit;
2. zet de Visit naar `Stopping`;
3. annuleert irrelevant toekomstig schedulerwerk;
4. handelt alle nog open actieve of scheduled provideractions duurzaam af;
5. gebruikt reconciliation wanneer een provideruitkomst onzeker is;
6. finaliseert pas wanneer geen providerwerk meer openstaat.

Een reeds geplande scheduled successor wordt dus eveneens opgeruimd.

## Eindtijd wijzigen

### Verkorten

Bij verkorten worden obsolete continuation- en terminal-workitems vervangen/geannuleerd. Scheduled successors die niet meer passen worden geannuleerd of vervangen. Als bestaande providerdekking voorbij de nieuwe Visitgrens loopt, zorgt duurzame `StopVisit`-work voor afhandeling op de nieuwe grens.

### Verlengen

Bij verlengen worden policy, parkeerregels en terminale grens opnieuw bepaald. Alleen extra betaalde dekking vóór de nieuwe effectieve Visitgrens wordt ingepland. Een eerdere elapsed- of paid-durationgrens blijft leidend.

## Capacity

Actieve Visit-lifecycle-states bezetten applicatiecapaciteit totdat de Visit definitief is beëindigd. De globale providerlimiet en eventuele per-user Visitlimieten worden server-side afgedwongen.

Een scheduled provideraction kan bij een provider wel of niet capaciteit tellen; dat exacte live 2Park-contract is niet hard gemeten. De applicatie leunt voor correctness niet op die aanname.

## Reconciliation en externe wijzigingen

Bij een onzekere provideruitkomst wordt niet blind opnieuw gemuteerd. De duurzame provideroperation gaat naar een onzeker/reconciliationpad en wordt via read-back bevestigd of verder onderzocht.

Bij onverwachte externe providerstate kan de Visit `AttentionRequired` worden. Nieuwe continuation wordt dan uitgesteld/geblokkeerd totdat de situatie veilig is. Terminal Stop-work blijft juist uitvoerbaar wanneer dat nodig is.

## Restart

V1 draait met exact één actieve `Parkeren.Api` / `VisitSchedulerWorker` instance. Bij startup wordt recovery eerst volledig uitgevoerd voordat nieuwe schedulerclaims beginnen.

Recovery herstelt onder andere:

- achtergelaten schedulerclaims;
- stale/incomplete provideroperations;
- onderbroken reconciliation;
- ontbrekende continuationplanning;
- ontbrekende of verouderde terminale `StopVisit`-work.

Daardoor blijven Visits en provideracties herstelbaar na een container- of procesrestart.
