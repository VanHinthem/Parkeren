# SCHED-003 — handmatig stoppen versus continuation

**Status:** ✅ Regressie geverifieerd na scheduler hardening  
**Afhankelijkheden:** SCHED-014, SCHED-015  
**Scenario:** een gebruiker/beheerder stopt een Visit terwijl continuation pending, claimed of extern in-flight is.

## Gewenste invariant

Stop moet altijd winnen. Na een stopclaim mag geen provideraction de Visit zelfstandig overleven en een in-flight continuation mag niet leiden tot blijvende nieuwe dekking.

## As-built na hardening

`PostgresStopVisitClaimer` en schedulerclaim/retry gebruiken nu dezelfde Visit-first lock-order. De stopclaim zet de Visit naar `Stopping`, annuleert nog relevante pending schedulerwork en maakt de duurzame Stop-operation aan voordat providercleanup verdergaat.

De centrale `VisitSchedulerWorkExecutionPolicy` geeft terminal `StopVisit` work voorrang op continuation en voorkomt dat tijdelijke health-state terminal cleanup definitief annuleert.

`StopVisitFlow` blijft alle open provideractions afhandelen totdat geen niet-terminale action meer resteert. Dat omvat zowel de actieve predecessor als een reeds aangemaakte future `scheduled` successor. `StopVisitFinalizer` rondt de Visit pas af wanneer alle provideractions terminal zijn.

De continuation-keten behoudt daarnaast de mutation guard en herlaadt Visit-state na externe calls. Als Stop tijdens een in-flight continuation wint, mag de result/recoveryflow de Visit niet terugzetten naar `Active`.

## Regressiebewijs

Na de hardening is het scenario opnieuw afgedekt door de combinatie van:

- PostgreSQL-racetests voor schedulerclaim versus manual Stop uit fase A2 (`4023c6e3`);
- centrale work execution policy uit fase A1, inclusief terminale Stop-semantiek;
- de JIT end-to-endtest waarin exact één future scheduled successor wordt gemaakt;
- `f581a135` (`test: cover stopping scheduled jit successor`), die expliciet bewijst dat de scheduled successor eerst wordt gestopt en daarna de actieve predecessor, waarna geen open provideraction achterblijft;
- startup/recovery hardening uit fase F, waardoor achtergelaten claims en provideroperations geen nieuw continuation-pad kunnen openen vóór recovery klaar is.

## Open providerpunten

Geen zelfstandig SCHED-003-gat resteert. Onbevestigd 2Park post-End gedrag en scheduled-capacity blijven providercontractvragen en veranderen de Stop-invariant niet: een lokaal bekende open provideraction van een stoppende Visit moet terminal worden afgehandeld.

## Conclusie

SCHED-003 is na de scheduler-hardening regressie-gevalideerd. De eerder gedeelde risico's uit SCHED-014 en SCHED-015 zijn opgelost en de specifieke scheduled-successor Stop-flow is end-to-end bewezen. Manual Stop blijft leidend boven continuation.
