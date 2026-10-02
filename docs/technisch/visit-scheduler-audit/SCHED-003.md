# SCHED-003 — handmatig stoppen versus continuation

**Status:** ✅ Audit afgerond; geen zelfstandige functionele afwijking gevonden  
**Afhankelijkheden:** SCHED-014, SCHED-015  
**Scenario:** een gebruiker/beheerder stopt een Visit terwijl continuation pending, claimed of extern in-flight is.

## Gewenste invariant

Stop moet altijd winnen. Na een stopclaim mag geen provideraction de Visit zelfstandig overleven en een in-flight continuation mag niet leiden tot blijvende nieuwe dekking.

## As-built gedrag

`PostgresStopVisitClaimer` gebruikt de Visit advisory lock, zet de Visit naar `Stopping`, annuleert alle nog `Pending` scheduler-items en maakt een duurzame root `ProviderOperation` van type `Stop`.

`StopVisitFlow` blijft vervolgens provideractions afhandelen totdat geen niet-terminale action meer resteert. `StopVisitFinalizer` rondt de Visit pas af wanneer alle provideractions `Stopped`, `Completed` of `Failed` zijn.

De continuation-keten bevat daarnaast meerdere stop-precedence beschermingen:

- de continuation mutation guard vereist `Active + Healthy`;
- result stores herladen de Visit na een externe call;
- wanneer Stop tijdens een in-flight start heeft gewonnen, blijft `Stopping` behouden en wordt de nieuwe action/recovery-state niet stilzwijgend terug naar `Active` geschreven;
- een later zichtbaar geworden successor valt onder de normale stopfinalization en moet dus ook worden beëindigd.

## Positieve testdekking

Er is integratiedekking voor stopclaim-replay, annuleren van pending schedulerwork en races rond provider-mutaties. De duurzame stopflow is duidelijk gescheiden van een simpele lokale statuswijziging.

## Gedeelde risico's

Dit scenario heeft twee afhankelijkheden die elders als zelfstandige bevinding zijn vastgelegd:

- [SCHED-014](SCHED-014.md): schedulerclaim en Stop hanteren niet overal dezelfde lockvolgorde; daardoor bestaat een deadlockvenster.
- [SCHED-015](SCHED-015.md): generieke `Active + Healthy` gating kan een gepland `StopVisit`-work annuleren wanneer de Visit health intussen afwijkt.

## Onduidelijkheden / open vragen

Geen aanvullende functionele vraag voor de normale handmatige stop. Wel moet bij het verbeterontwerp expliciet bewezen worden dat een Stop tijdens een reeds uitgevoerde maar nog niet lokaal bevestigde scheduled successor altijd beide provideractions opruimt.

## Conclusie

De stop-precedence architectuur zelf is solide en defensief. Er is geen aparte SCHED-003-fix nodig, maar SCHED-003 kan pas als volledig betrouwbaar worden beschouwd nadat SCHED-014 en SCHED-015 zijn opgelost en met een race-test zijn bewezen.