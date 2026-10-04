# Implementatieplan resterend V1-werk

**Reviewdatum:** 4 oktober 2026
**GitHub-planning:** [issue #92](https://github.com/VanHinthem/Parkeren/issues/92)
**Scope:** de 15 issues die op de reviewdatum openstonden; #84 is na de closeout-audit gesloten, dus 14 issues blijven open.

## Uitkomst relevantiecheck

Geen van de 15 issues die bij de eerste review openstonden was duidelijk achterhaald. Meerdere issues zijn wel gedeeltelijk gerealiseerd of vragen vooral nog een acceptatie-audit. Bouw die onderdelen niet opnieuw: werk de GitHub-checklists bij op basis van bestaand bewijs en maak alleen concrete ontbrekende punten tot implementatiewerk. #84 is na verificatie van alle criteria gesloten; de overige issues blijven open totdat hun gates zijn aangetoond.

| Issue | Huidige stand en resterende reden om open te blijven |
| --- | --- |
| [#94 PWA-hardening](https://github.com/VanHinthem/Parkeren/issues/94) | **Afgerond 4 oktober 2026.** Build-id, offlineveiligheid, manifest/service-workerchecks, toegankelijkheidsbasis en JS/CSS-budget zijn geïmplementeerd. CI-run [37207178116](https://github.com/VanHinthem/Parkeren/actions/runs/37207178116) slaagde; echte iOS/Android-devicevalidatie blijft bij #78. |
| [#93 Historie-import](https://github.com/VanHinthem/Parkeren/issues/93) | Actief. De 2Park-historie-reader bestaat, maar een idempotente import van oude transacties en opname in het Oss-budget is niet aangetroffen. |
| [#92 V1-plan](https://github.com/VanHinthem/Parkeren/issues/92) | **Afgerond 4 oktober 2026.** De uitvoerbare fasering en afhankelijkheden zijn vastgelegd in dit document. Resterend implementatie- en releasewerk wordt gevolgd via de betreffende issues en releasegates. |
| [#86 2Park-mock](https://github.com/VanHinthem/Parkeren/issues/86) | Grotendeels gerealiseerd. Mockserver en failure-injection bestaan; de issue meldt zelf dat de basis klaar is. Resterende contractdetails hangen af van #71. Rond af met een checklist-audit, geen nieuwe mock vanaf nul. |
| [#84 Transactionele Stop](https://github.com/VanHinthem/Parkeren/issues/84) | **Gesloten 4 oktober 2026.** Beide resterende criteria zijn getest: [Stop versus eindtijdwijziging](../../tests/Parkeren.IntegrationTests/Database/VisitSchedulerLockingTests.cs) en [pushfout na succesvolle Stop](../../tests/Parkeren.IntegrationTests/Database/PushDeliveryProcessorTests.cs). De twee betrokken testklassen slagen met 19/19 tests. |
| [#82 Domeinmodel/invarianten](https://github.com/VanHinthem/Parkeren/issues/82) | Checklist 60/60 afgehandeld op 4 oktober 2026; GitHub-issue is gereed voor closeout. Zie de [criterium-naar-code/schema/test-matrix](issue-82-evidence-map.md). Niet-terminale Visits tellen lokaal als slots; vijf actieve provideractions zijn bevestigd, terwijl `scheduled`-capaciteit en overflow als niet-blokkerende risico's zijn geaccepteerd. Lost-start matching gebruikt één unieke kandidaat op product, genormaliseerd kenteken, status en tijden; tijdelijke provider-readback na responseverlies blijft een geaccepteerd risico. Het onderscheid `Stopped`/natuurlijke `Completed` is eveneens onbevestigd en geaccepteerd. #71 is afgesloten; er zijn geen nieuwe live tests gepland. Testhistorie staat hieronder. |
| [#78 PWA-capabilityvalidatie](https://github.com/VanHinthem/Parkeren/issues/78) | Actief. Android is gevalideerd; de iOS-matrix, offline herstel, meerdere devices/sessie-intrekking, subscription-refresh en productie-hostcheck staan nog open. Zie de [capabilitymatrix](fase-7-pwa-capability-matrix.md). |
| [#73 Visit-capaciteit](https://github.com/VanHinthem/Parkeren/issues/73) | Gedeeltelijk gerealiseerd. De lokale globale en per-user capaciteit wordt atomair geclaimd en getest. Maximaal vijf actieve provideractions is bevestigd; de beheerafspraak houdt `MaxConcurrentVisits` op maximaal vijf zonder harde codegrens. Of `scheduled` actions capaciteit reserveren en welke overflowresponse volgt, is onbevestigd en als niet-blokkerend risico geaccepteerd in #71. Dashboard-, admin- en overige providerlimietcriteria blijven in #73. |
| [#71 2Park-gedrag](https://github.com/VanHinthem/Parkeren/issues/71) | **Afgesloten 4 oktober 2026.** Live bewezen: directe action-ID/read-back, maximaal vijf actieve provideractions, 240 minuten geaccepteerd en 241 geweigerd (`PRK-00067`), toekomstige `scheduled` action met pre-start annulering, exacte aansluiting geweigerd (`PRK-00005`) en `End + 1 seconde` geaccepteerd. Live read-back kan locatie als label tonen en timestamps enkele seconden laten afwijken. T-5 is lokale policy, geen provider-SLA; lokale Unknown/reconciliation voorkomt blinde retries. Scheduled-capacity, read-back na responseverlies, post-End-zichtbaarheid, action-ID-scope, timestamp-SLA en overige foutcategorieën zijn als niet-blokkerend risico vastgelegd. `extend_action` wijzigde de provider-eindtijd niet persistent bij de T-60-proef. Geen nieuwe live tests gepland. |
| [#68 2Park-storingen](https://github.com/VanHinthem/Parkeren/issues/68) | Gedeeltelijk gerealiseerd. Duurzame operations en unknown-outcome reconciliation bestaan. Getypeerde provider-responsefouten bij Start, continuation en Stop worden als `Unknown` opgeslagen met provider-code en reconciliatie; Stop wordt na zo'n fout niet definitief afgerond. De bredere foutclassificatie/backoff en functionele meldingen wanneer providerdekking tijdens gratis tijd nog niet bevestigd is, vragen nog verificatie en mogelijk implementatie. |
| [#64 Account/product-sync](https://github.com/VanHinthem/Parkeren/issues/64) | Gedeeltelijk gerealiseerd. Productcatalogus wordt bij iedere applicatiestart opnieuw gesynchroniseerd; een providerfout wordt gewaarschuwd en blokkeert startup niet. De laatste succesvolle balans, eenheid en ophaaltijd zijn per product persistent; één conditionele database-update voorkomt dat een oudere refresh een nieuwere snapshot overschrijft. Resterend: credentialvalidatie, periodieke refresh en veilige coördinatie met mutaties. |
| [#11 Deactiveren/archiveren](https://github.com/VanHinthem/Parkeren/issues/11) | **Gesloten 4 oktober 2026** na implementatie en review. API/UI ondersteunen veilig archiveren en verwijderen; PostgreSQL-tests bewijzen Visit-blokkade, statusconcurrency en historiebehoud. Commit `e2c1ee7`. |
| [#8 2Park-integratie-epic](https://github.com/VanHinthem/Parkeren/issues/8) | Relevant als parent van de nog open provider-sync-, storings- en capaciteitswerkzaamheden (#64, #68, #73) en de bijbehorende validatie. Geen apart implementatieproject naast de children. |
| [#6 Monitoring/notificaties-epic](https://github.com/VanHinthem/Parkeren/issues/6) | Relevant als parent; de issue meldt dat #78 de resterende child is. Sluit na afronding van die capability-spike. |
| [#1 Bezoekers/toegang-epic](https://github.com/VanHinthem/Parkeren/issues/1) | **Gesloten 4 oktober 2026** nadat alle zes children (#9–#12, #69–#70) gecontroleerd gesloten bleken. |

## Uitvoeringsvolgorde

De tracks hieronder kunnen naast elkaar lopen zodra hun expliciete afhankelijkheden zijn voldaan. GitHub blijft leidend voor status, eigenaar en voortgang.

### Gate 0 — Afsluiten wat al gebouwd is

**Update 4 oktober 2026:** #82 heeft PostgreSQL-overlapconstraints voor ParkingRuleSet, tariff en budgetperioden en directe loggercaptures voor de VisitRecovery Start-retryafwijzing. De Release-integratiesuite slaagde destijds met 262/262; latere gerichte captures en tests zijn apart gerapporteerd. `btree_gist` vereist extensie-installatierechten voor de migratierol. Het besluit is geen database-singletonconstraint voor `DefaultParkingPolicy`; #71 is afgesloten en resterende provideronzekerheden zijn waar passend als niet-blokkerend risico geaccepteerd.

De #82-matrix staat in [issue-82-evidence-map.md](issue-82-evidence-map.md). De processor-, sender-, hosted-worker- en schedulerlogtests bevestigen dat exceptions niet aan de logger worden doorgegeven en dat veilige IDs, retrystatus en foutafhandeling behouden blijven. De aanvullende startup-, diagnostische cleanup- en Start-retrypaden loggen vaste meldingen met relevante correlatie-ID's; directe captures dekken product-sync, de drie dev-cleanupvarianten en Start-retry, terwijl overige paden code-reviewbewijs hebben. De volledige solution-suite is na deze batch 478/478 geslaagd (4 oktober 2026). Voor #82 resteren drie criteria, vastgelegd in de evidence map; #71 is afgesloten en er zijn geen nieuwe live tests gepland. Sluit #82 pas wanneer elk resterend criterium bewezen is of expliciet als risico is geaccepteerd. #84 is gesloten nadat de aanvullende Stop-versus-eindtijd- en pushfailure-tests groen waren. Herhaal geen al bewezen provider- of mockimplementatie.

**Vervolg loggercaptures 4 oktober 2026:** directe tests dekken het falende startup-product-sync-pad (`Initial_product_sync_failure_does_not_log_exception_object_or_details`) en scheduler startup-recovery vóór work-claims (`Startup_recovery_retries_and_claims_only_after_success_without_logging_exception_details`). De aangescherpte retrytest wacht op de tweede recoverypoging, controleert nul claims terwijl die poging geblokkeerd is en geeft pas daarna herstel vrij. De gerichte Release-regressieset slaagde met 5/5; de volledige suite is na deze wijziging niet opnieuw gedraaid. De oudere 478/478 solution-suite en 262/262 Release-integratiesuite zijn historische resultaten.

**Retentie follow-up 4 oktober 2026:** `Retention_cleanup_deletes_only_notifications_older_than_cutoff` dekt zowel de defaultretentie van 90 dagen als een ingestelde 30 dagen en bevestigt dat een scheduler-audit-event behouden blijft. De gerichte Release-test slaagde met 1/1; de volledige suite is niet opnieuw gedraaid.

**Vervolg logger- en retentietests 4 oktober 2026:** de drie dev-diagnostic cleanup-routes gebruiken nu `DiagnosticActionCleanup`; captures testen elk waarschuwingtemplate en het succespad zonder exception-details (`ProgramStartupLoggingTests`, 5/5 inclusief startup-productsync). `Retention_cleanup_rejects_nonpositive_retention_days` controleert nul en negatieve waarden; de retentieset slaagde met 3/3. De volledige suite is na deze wijzigingen niet opnieuw gedraaid.

**#82-checklist bijgewerkt, 4 oktober 2026:** 60/60 criteria zijn afgehandeld. Besluiten: `DefaultParkingPolicy` blijft applicatie-side geserialiseerd en update-in-place; persistente absolute tijden gebruiken PostgreSQL `timestamp with time zone` met representatieve `+02:00` round-tripdekking; de lokale `ProviderParkingAction`-state-machine is definitief; `Stopped` vereist bevestiging of vastgestelde afwezigheid, terwijl `Completed` alleen lokale verstreken geplande dekking betekent; statusmapping is conservatief en onbekende statussen veroorzaken geen terminale transitie. Het niet-bewezen onderscheid tussen `Stopped` en natuurlijke `Completed` is als risico geaccepteerd. Na verloren Start-response wordt alleen een unieke kandidaat op product, genormaliseerd kenteken, `active`/`scheduled` en tijden binnen vijf seconden bevestigd; provider-readbackbeschikbaarheid is onbevestigd en geaccepteerd. Lokale capaciteit telt niet-terminale Visits; providerlimiet is maximaal vijf actieve actions. Of `scheduled` provider-capaciteit reserveert en welke overflowresponse volgt, is als niet-blokkerend risico geaccepteerd. Er staan geen checklistpunten open.

**Aanvullend #82-testbewijs:** de Release-integratietests `Default_change_is_allowed_when_active_user_overrides_changed_field`, `Paid_start_with_offset_timestamps_persists_utc_provider_end` en `Login_persists_pin_and_session_token_only_as_hashes` slaagden gericht met 3/3. Ze bewijzen respectievelijk applicatie-side update-in-place zonder rijgroei, UTC-offsets voor geselecteerde Visit-/provider-action-/scheduler-workvelden na een `+02:00` Start, en opslag van PIN-/sessietokenhashes zonder de ruwe waarden. De UTC-test is representatief, niet exhaustief per tijdveld; in combinatie met de gedeelde PostgreSQL instantmapping is de UTC-beslissing vastgelegd. De singletonbeslissing is eveneens vastgelegd. De actuele checkliststand is 60/60; er staan geen checklistpunten open.

Voor #86 en het lokale deel van #73 gelden de bevindingen en risicoacceptaties uit de afgesloten #71-audit. Werk issuebeschrijvingen en checklists bij met concrete testnamen en bewijs; voer geen nieuwe live providerproeven uit. Alleen een ontbrekend criterium wordt een nieuwe taak.

**Exit:** ieder criterium van #82 is aantoonbaar afgedekt of heeft een concrete resterende taak; de issue wordt niet gesloten zolang bewijsleemtes of #71-afhankelijkheden onopgelost zijn. #84 is gesloten met testbewijs; de bestaande delen van #86/#73 zijn expliciet gemarkeerd als gerealiseerd.

### Provider- en recoverytrack

1. **Beheer #71 als residual-risk registratie.** Er zijn geen nieuwe live tests gepland. T-5 blijft lokale policy; maximaal vijf Visits is de beheerafspraak/configuratie, zonder harde codegrens. Laat onbevestigde providersemantiek expliciet als zodanig staan; forceer geen response-loss-, overflow-, saldo- of action-ketentests.
2. **Werk #86 bij op basis van #71.** Houd bewezen providercontract en configureerbaar mockgedrag gescheiden. Voeg alleen ontbrekende fixtures/failure-cases en reproduceerbare CI/containerchecks toe.
3. **Rond #64 af.** Maak de status van credentials/product en saldo-freshness beheerbaar en betrouwbaar; blokkeer nieuwe provideracties bij ongeldige configuratie, zonder bestaande Visits onnodig te blokkeren bij niet-kritieke refreshfouten. Persist de relevante laatste-successtatus en test sync/mutatie-concurrency.
4. **Rond #68 af.** Classificeer tijdelijke, configuratie-, validatie-, saldo- en onzekere fouten; specificeer retry/backoff en reconcileer vóór herhaling van onzekere mutaties. Verifieer visitor-/adminmeldingen en het gratis-tijdscenario, server-side en onafhankelijk van pushdelivery.
5. **Voltooi #73.** Behoud de reeds concurrency-safe lokale limiet. Voeg alleen de providerlimiet-/foutafhandeling en resterende zichtbaarheid toe die #71 bevestigt; test gelijktijdige starts, scheduled acties en capacity-release na definitieve Stop.
6. Sluit parent **#8** wanneer #64, #68 en #73 klaar zijn en relevante providerafhankelijkheden van #71/#86 zijn geaccepteerd.

**Exit:** geen provideractie wordt blind herhaald; lokale en providerlimieten zijn onderscheiden; tests bewijzen herstel, foutclassificatie, capaciteit en veilige statusweergave.

### PWA-track

1. **Rond #78 af op echte geïnstalleerde clients.** Werk per open matrixrij een iOS/Android-resultaat, apparaat- en OS-/browserversie en beperking bij. Test offline herstel, sessie-intrekking over meerdere devices, subscription-refresh en productie-HTTPS/service-worker.
2. **Voer daarna #94 uit.** Voeg automatisch build-id toe, maak offline/netwerkverlies zichtbaar zonder stale data voor parkeeracties, valideer manifestinstellingen en voeg CI-regressiechecks plus een eenvoudig performancebudget toe.
3. Sluit parent **#6** na afronding van #78; houd #94 als afzonderlijk hardeningissue totdat zijn eigen criteria slagen.

**Exit:** capabilitymatrix en versie-/offline-/performancecontroles zijn reproduceerbaar; mutaties kunnen niet ten onrechte als succesvol worden gepresenteerd bij offline/stale state.

### Data- en toegangsbeheertrack

- **#11:** gesloten op 4 oktober 2026. Archiveren behoudt historie en blokkeert bij actieve Visits; permanent verwijderen is beperkt tot records zonder parkeerhistorie.
- **#93:** bouw pas na bevestiging van providerdata en budgetsemantiek een idempotente import met expliciete herkomst en provider-ID. Importeer gerealiseerd providerverbruik zonder actuele parkeerregels over oude transacties heen te rekenen; test herhaalde sync, correcties en samenvoeging met nieuwe Visits.
- Parent **#1** is gesloten nadat alle zes children (#9–#12, #69–#70) gecontroleerd gesloten bleken.

**Exit:** import is herhaalbaar zonder duplicaten en sluit aan op het Oss-budget; gebruikers-/voertuighistorie blijft intact bij archivering/verwijdering.

## Release- en documentatiegates

- Werk na iedere slice de betreffende GitHub-checklist en functionele/technische documentatie bij.
- Voer gerichte tests uit tijdens de slice en de relevante volledige suites vóór merge; providerproeven blijven beperkt tot het expliciete testaccount.
- Behandel #82 als afrondwerk zolang de audit geen ontbrekend gedrag aantoont. #84 is gesloten met checklist- en testbewijs (19/19 gerichte tests).
- Sluit epics pas nadat hun children zijn afgerond. #92 is gesloten als planningsdeliverable; resterende implementatie- en deployment/releasebesluiten blijven bij de betreffende issues en releasegates.
