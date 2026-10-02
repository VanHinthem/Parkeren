# SCHED-005 — `DesiredEndAt` verlengen

**Status:** ✅ Opnieuw geverifieerd na scheduler-hardening; geen zelfstandige afwijking gevonden  
**Afhankelijkheden:** gedeelde continuation-, recovery- en lockingpaden  
**Scenario:** een actieve Visit met concrete eindtijd wordt naar later verlengd.

## Gewenste invariant

Een geldige verlenging moet de Visit-eindtijd duurzaam wijzigen en precies één continuation-pad creëren wanneer de bestaande providerdekking niet tot de nieuwe eindtijd reikt. Gratis tijd mag geen provideraction veroorzaken.

## Huidig as-built gedrag

`PostgresVisitEndTimeChanger`:

- serialiseert de wijziging via de Visit advisory lock;
- valideert opnieuw tegen de Visit-policy en toepasselijke rulesets;
- wijzigt `DesiredEndAt` duurzaam;
- roept bij verlenging de continuationplanning aan wanneer extra betaalde dekking nodig is;
- maakt geen duplicate `ContinueProviderCoverage` wanneer al relevant `Pending` of `Claimed` work bestaat;
- plant bij aaneengesloten betaald parkeren op T-5 van de huidige provider-end;
- plant na een echte gratis periode op **T-5 van `nextPaid.Start`**;
- maakt geen providerwork wanneer de verlenging uitsluitend gratis tijd toevoegt;
- herbouwt terminal work via dezelfde centrale terminal-boundarylogica, zodat verlenging en terminal lifecycle niet uit elkaar kunnen lopen.

De uitvoering daarna gebruikt dezelfde geharde continuationketen als normale coverage:

- JIT future successor binnen T-5;
- maximaal één successor per Visit;
- `scheduled` geldt als succesvolle providerstart;
- bekende scheduled successor wordt niet gedupliceerd bij replay/recovery;
- Unknown/reconciliation leidt niet tot een blinde tweede provider-mutation;
- provider matching gebruikt de centrale product/plate/status/timestamp-policy met 5-seconden engineering tolerance.

## Testdekking

De bestaande scheduler- en integratietests bewijzen onder meer:

- verlengen naar betaald parkeren maakt continuation-work;
- aaneengesloten coverage gebruikt T-5;
- coverage na een gratis gat gebruikt T-5 van de volgende betaalde boundary;
- verlengen uitsluitend in gratis tijd maakt geen provideraction;
- replay maakt geen duplicate schedulerwork;
- continuation-start en recovery behouden de single-successor invariant;
- een reeds geplande scheduled successor wordt bij recovery niet opnieuw aangemaakt.

## Eerdere gedeelde bevindingen — opgelost

De blockers uit de oorspronkelijke audit zijn inmiddels verwerkt:

- **SCHED-001:** continuation wordt JIT vooraf aangemaakt en `scheduled` is een geldige bevestigde providerstatus;
- **SCHED-002:** hervatten na gratis tijd gebeurt vanaf T-5 vóór de volgende betaalde boundary, met `PlannedStartAt = nextPaid.Start`;
- **SCHED-014:** end-time change en schedulerclaim gebruiken dezelfde Visit-first lock-order;
- **SCHED-009/010:** unresolved provideroperations worden eerst gereconcilieerd en recovery dupliceert bestaande scheduled successors niet.

## Resterende provideronzekerheden

Geen unieke SCHED-005-functionele onzekerheid resteert. Algemeen providercontract dat nog live bevestigd moet worden — zoals post-End zichtbaarheid en of scheduled actions meetellen voor providercapaciteit — blijft expliciet configureerbaar in TwoParkMock en verandert de single-successor/extension-invariant niet.

## Conclusie

`DesiredEndAt` verlengen is na de scheduler-hardening opnieuw integraal beoordeeld. De wijziging blijft durable, gebruikt de centrale paid/free-segmentatie, plant continuation op de correcte precheck-boundary en behoudt idempotent precies één continuationpad. Er resteert geen zelfstandig SCHED-005-gat.