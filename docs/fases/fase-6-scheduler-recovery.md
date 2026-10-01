# Fase 6 — Scheduler, continuation en recovery

**Status: afgerond ✅**  
**Afgesloten: 30 september 2026**

## Doel

Fase 6 maakt de Visit-lifecycle onafhankelijk van een geopende PWA of in-memory timers. Providerdekking, toekomstige acties, retries en herstel worden persistent en server-side georkestreerd.

## Opgeleverd

- Persistente `VisitSchedulerWork` met atomair PostgreSQL-claiming.
- Server-side `VisitSchedulerWorker` met startup recovery.
- JIT provider-continuation met precheck op T-5 minuten.
- Ruleset-/paid-window-gestuurde providerdekking; geen dekking in gratis perioden.
- Rolling planning horizon van 14 dagen voor volledig open-ended Visits.
- Visit-level advisory locking en Stop-precedence.
- Persistente `ProviderOperation` correlation/idempotency voor provider-mutaties.
- Unknown outcome -> reconciliation in plaats van blind retry.
- Recovery na proces/container-restart.
- Periodieke reconciliation van unknown operations en bekende actieve provider-actions.
- Duurzame cancel/replace van scheduled provider-actions bij end-time changes.
- Durable active shortening: `StopVisit` scheduler-work op de nieuwe `DesiredEndAt`.
- Stop Visit beëindigt/cancelt alle relevante actieve/geplande providerdekking voordat de Visit definitief wordt afgerond.
- Integration tests voor scheduler claiming, continuation, stop-races, replay/recovery, cancellation/replacement en active shortening.

## Functionele beslissingen

### Provider continuation is geen user-opt-in

Een geldige actieve Visit moet automatisch providerdekking houden. `AllowVisitExtension` bepaalt alleen of een gebruiker `DesiredEndAt` later mag zetten; `AllowOpenEndedVisits` bepaalt of `DesiredEndAt = null` is toegestaan.

### Oss gebruikt StartNewAction

Live tests tegen 2Park bevestigden dat `extend_action.json` wel `OK/SUCCESS` kan retourneren zonder de eindtijd van een actieve action persistent te wijzigen. Ook een JIT-poging op T-60 wijzigde de action niet.

Voor Oss:

```text
MaxProviderActionDuration = 4 uur
Continuation              = StartNewAction
```

Een successor wordt JIT aangemaakt. Exact aansluitende actions worden door 2Park als overlap geweigerd; een start na de vorige provider-eindtijd wordt gebruikt.

### Active shortening

Een actieve provider-action wordt niet onmiddellijk gestopt wanneer de gebruiker een latere toekomstige Visit-eindtijd naar voren haalt. De nieuwe `DesiredEndAt` wordt vastgelegd en de scheduler krijgt persistent `StopVisit`-work voor dat tijdstip.

### Open-ended planning

De 14-daagse horizon is uitsluitend technisch. Hij is geen maximale Visitduur.

## Technische beslissingen

- Scheduler correctness is database-backed; geen correctness-afhankelijkheid van process memory.
- Due work wordt geclaimd met `FOR UPDATE SKIP LOCKED`.
- Scheduler, Stop en end-time change serialiseren per Visit met PostgreSQL advisory locks.
- Scheduler work-id wordt gebruikt als stabiele correlation/idempotency-key waar van toepassing.
- Provider-mutatie wordt vóór uitvoering persistent vastgelegd.
- Een onzekere provideruitkomst wordt eerst gereconcilieerd.
- Startup recovery moet slagen voordat nieuw schedulerwerk geclaimd wordt.
- De provider wordt opnieuw gelezen op relevante grenzen; lokale state alleen is niet voldoende.
- Stop heeft voorrang op continuation.
- Functionele Visit-state is leidend; scheduler-work is een vervangbaar uitvoeringsplan.

## Documentatie

- `docs/technisch/visit-scheduler.md` — functionele en technische schedulerarchitectuur.
- `docs/functioneel/parkeer-en-visitparameters.md` — policy/rules/continuation-semantiek.
- `docs/functioneel/parkeerbeleid.md` — actuele policynamen en 2Park/Oss-keuzes.

## Fase-6 exit

De Fase-6 exit is behaald: actieve Visits kunnen server-side correct worden voortgezet en hersteld bij restart, retries, timeouts en races zonder afhankelijkheid van telefoon/PWA-timers.

## Bewust doorgeschoven

Deze punten horen bij bredere stories en blokkeren de Fase-6 scheduler/recovery-exit niet:

- Notificatie-inbox, pushdelivery en recipient rules -> Fase 7 (#53–#56, #76–#80).
- Beheerweergave van discrepancies/externe providerhistorie -> Fase 8 (#63 en resterende delen #66).
- Volledige echte-provider hardening, aanvullende JIT/capacity/errorproeven -> Fase 9 / #71.
- Providerhistorie en Oss-jaarbudget import -> #93.
- Definitieve ownership/naam van globale providercapaciteit -> #67/#73.
- Volledige retry/backoff- en providerfout-UX waar die buiten scheduler correctness valt -> resterende delen #68.

Open issues blijven open wanneer hun acceptance criteria deze latere scope bevatten; Fase 6 wordt dus niet administratief “groen” gemaakt door onaf werk af te vinken.
