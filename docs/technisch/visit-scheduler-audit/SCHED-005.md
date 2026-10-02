# SCHED-005 — `DesiredEndAt` verlengen

**Status:** ✅ Audit afgerond; geen zelfstandige afwijking gevonden  
**Afhankelijkheden:** SCHED-001, SCHED-014  
**Scenario:** een actieve Visit met concrete eindtijd wordt naar later verlengd.

## Gewenste invariant

Een geldige verlenging moet de Visit-eindtijd duurzaam wijzigen en precies één continuation-pad creëren wanneer de bestaande providerdekking niet tot de nieuwe eindtijd reikt. Gratis tijd mag geen provideraction veroorzaken.

## As-built gedrag

`PostgresVisitEndTimeChanger`:

- serialiseert de wijziging via de Visit advisory lock;
- valideert opnieuw tegen de Visit-policy en toepasselijke rulesets;
- wijzigt `DesiredEndAt`;
- roept bij verlenging `EnsureContinuationWorkAsync` aan;
- maakt geen duplicate `ContinueProviderCoverage` wanneer al `Pending` of `Claimed` work bestaat;
- plant bij aaneengesloten betaald parkeren op T-5 van de huidige action;
- plant na een gratis gat op het begin van het volgende betaalde segment;
- maakt geen work wanneer de verlenging alleen gratis tijd toevoegt.

## Testdekking

`VisitEndTimeSchedulerTests` dekt expliciet:

- verlengen naar betaald parkeren maakt continuation-work;
- de due-time is T-5 bij aaneengesloten dekking;
- verlengen uitsluitend in gratis tijd maakt geen providerwork;
- replay van dezelfde end-time operation maakt geen duplicate schedulerwork.

## Afhankelijkheden

De orchestration van de verlenging is correct, maar het uiteindelijke gedrag erna gebruikt dezelfde continuationpaden als andere scenario's:

- [SCHED-001](SCHED-001.md): T-5 `StartNewAction` is momenteel intern tegenstrijdig.
- [SCHED-002](SCHED-002.md): hervatten na gratis tijd wordt pas op `nextPaid.Start` uitgevoerd.
- [SCHED-014](SCHED-014.md): lock-order tussen end-time change en schedulerclaim is niet consistent.

## Onduidelijkheden / open vragen

Geen unieke functionele open vraag. Bij het uiteindelijke verbeterplan moet wel getest worden dat een verlenging die plaatsvindt terwijl een scheduled successor al bestaat die successor correct hergebruikt of vervangt en nooit een tweede successor naast de eerste creëert.

## Conclusie

Voor `DesiredEndAt` verlengen is geen zelfstandige ontwerpbug gevonden. De flow kan behouden blijven en moet na oplossing van de gedeelde continuation- en lockingbevindingen opnieuw integraal worden bewezen.