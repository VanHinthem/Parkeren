# SCHED-009 — provider timeout / unknown continuation

**Status:** ✅ Opnieuw geverifieerd na matching- en recovery-hardening  
**Prioriteit:** hoog

## Gewenste invariant

Na een timeout, netwerkfout of onduidelijke providerresponse mag dezelfde mutation niet blind opnieuw worden uitgevoerd. Eerst moet via read-back/reconciliation worden vastgesteld of de eerdere provideractie wel of niet heeft plaatsgevonden.

## Huidig as-built gedrag

De providerflows bewaren de mutation-intentie duurzaam vóór de externe call in `ProviderOperation` en `ProviderParkingAction`. Bij replay wordt een bestaande `Unknown`, `Reconciling` of nog geldige `InProgress` attempt niet blind opnieuw gemuteerd.

Voor provideridentificatie geldt inmiddels één centrale `ProviderActionMatchPolicy`:

1. bekende provider action-id wint altijd;
2. productcontext moet overeenkomen;
3. kenteken wordt genormaliseerd;
4. status moet semantisch toegestaan zijn;
5. Start/End gebruiken dezelfde centrale tolerance van 5 seconden.

Die 5 seconden zijn een engineering margin en geen gemeten 2Park-SLA.

Bij onbekend action-id accepteert fallback uitsluitend exact één unieke kandidaat. Een bekende action-id mag nooit naar een andere semantische kandidaat terugvallen. Future starts mogen `scheduled` teruggeven en worden daarmee correct herkend.

## Unknown / reconciliation

Een onzekere mutation blijft eerst `Unknown`/`Reconciling`. Read-back bepaalt daarna of dezelfde provideractie bestaat. Pas bij voldoende bewijs wordt de lokale operation bevestigd. Bij onvoldoende of ambigue evidence blijft de toestand onzeker; er volgt geen blind duplicate mutation.

Dit geldt voor Start, ContinueStart, Extend en Stop. Stop blijft bewust primair action-id driven en krijgt geen brede fallback-identificatie.

Startup recovery behandelt bovendien stale `InProgress` provideroperations pas na de bestaande vijf-minuten attempt lease. De operation wordt dan eerst naar `Unknown` gebracht en vervolgens gereconcilieerd; interrupted reconciliation wordt op dezelfde manier hervat.

## Regressiebewijs

De huidige tests bewijzen onder meer:

- fresh/in-progress replay veroorzaakt geen tweede providerstart;
- stale in-progress Start wordt eerst gereconcilieerd;
- `Unknown` wordt nooit blind opnieuw gemuteerd;
- persisted providerresponse kan na restart worden bevestigd zonder tweede Start;
- future `scheduled` actions worden als geldige Start/ContinueStart herkend;
- timestampdrift binnen 5 seconden matcht; buiten de tolerance niet;
- fallback zonder action-id vereist één unieke kandidaat;
- bekende action-id mismatch valt nooit terug naar een andere action;
- Unknown continuation/restart maakt geen tweede provideraction;
- Stop reconciliation stuurt geen tweede Stop zolang de eerdere uitkomst onzeker is.

TwoParkMock kan daarnaast unknown-after-write, delayed visibility en configureerbare read-backafwijkingen deterministisch simuleren, zodat boundary/recoverytests geen wall-clock wachttijden nodig hebben.

## Relaties

- [SCHED-001](SCHED-001.md): future continuation gebruikt dezelfde matching- en unknown-semantiek.
- [SCHED-010](SCHED-010.md): restart recovery hervat unresolved provideroperations vóór nieuwe schedulerclaims.
- [SCHED-017](SCHED-017.md): centrale provideridentity en timestamp tolerance.
- [SCHED-016](SCHED-016.md): deterministische provider test harness.

## Resterend extern contractpunt

Het exacte live 2Park post-End gedrag en een eventuele gemeten timestamp-SLA zijn nog geen bewezen providercontract. De applicatie leunt daar voor duplicate-prevention niet op: bekende IDs, unieke fallback en conservatieve Unknown-semantiek blijven leidend.

## Conclusie

Het oorspronkelijke risico uit deze audit is opgelost. Provider timeouts en onduidelijke responses worden duurzaam, idempotent en conservatief afgehandeld via read-back/reconciliation; centrale matching voorkomt de eerdere false-Unknown door millisecondevergelijkingen of `active`-only aannames. Er resteert geen zelfstandig SCHED-009-gat.