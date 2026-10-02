# SCHED-015 — generieke `Active + Healthy` gating past niet bij ieder work-type

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Raakt:** `StopVisit`, `LongVisitWarning` en mogelijk toekomstige scheduler-worktypes.

## Samenvatting

`PostgresVisitSchedulerWorkClaimer` beoordeelt vóór het claimen niet het work-type. Ieder due workitem wordt alleen geclaimd wanneer de Visit zowel `Active` als `Healthy` is. Anders wordt het work direct `Cancelled`.

Dat is passend voor provider-**continuation**: bij `Reconciling` of `AttentionRequired` moet automatische nieuwe providerdekking inderdaad niet blind doorgaan. Voor andere work-types heeft dezelfde regel echter een andere betekenis.

## `StopVisit`

Een deferred `StopVisit` kan juist noodzakelijk zijn wanneer een Visit technisch ongezond is. Bijvoorbeeld na shortening staat er bewust een stop op een functionele eindtijd. Als health vóór die tijd verandert naar `Reconciling` of `AttentionRequired`, annuleert de claimer het Stop-work voordat `ProcessScheduledStopAsync` het kan uitvoeren.

Daarmee kan providerdekking langer bestaan dan de expliciete Visit-eindtijd.

## `LongVisitWarning`

De processor zelf vereist alleen `VisitStatus.Active`. Toch bereikt een actieve maar niet-Healthy Visit de processor nooit: het warning-work wordt door de claimer gecanceld. Zie [SCHED-012](SCHED-012.md).

## `ReleaseFailedAsync`

Ook een al geclaimd workitem wordt na een processor-exception alleen opnieuw vrijgegeven wanneer de Visit `Active + Healthy` is; anders wordt het geannuleerd. Daardoor zit dezelfde generieke semantiek op retry-niveau.

## Gewenste invariant

Claimability moet per work-type worden bepaald. Bijvoorbeeld conceptueel:

- `ContinueProviderCoverage`: alleen wanneer provider-mutatie veilig is, waarschijnlijk `Active + Healthy`.
- `StopVisit`: uitvoeren op basis van lifecycle/eindtijd, ook bij bepaalde ongezonde healthstatussen.
- `LongVisitWarning`: functionele keuze op basis van `Active`, niet impliciet gekoppeld aan provider health.

De exacte matrix moet tijdens het verbeterontwerp worden vastgesteld.

## Onduidelijkheden / open vragen

1. Welke Visit-healthstatussen mogen `StopVisit` blokkeren, als die er überhaupt zijn?
2. Moet `AttentionRequired` een Long Visit warning naast de attention-notificatie blijven produceren?
3. Moet work bij een tijdelijk ongeschikte state worden `Cancelled` of juist later opnieuw worden beoordeeld?
4. Is `Status != Active` voor alle drie work-types wel dezelfde terminale reden?

## Richting voor later ontwerp

Centraliseer een expliciete work-type/state policy in plaats van één generieke claimercheck. De processor en claimer moeten dezelfde semantiek delen zodat een branch in de processor niet onbereikbaar wordt door een strengere generieke gate ervoor.

## Verificatiecriteria

Tests moeten per work-type minimaal `Healthy`, `Reconciling`, `AttentionRequired`, `Stopping` en terminale Visitstatussen afdekken.