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
| [#92 V1-plan](https://github.com/VanHinthem/Parkeren/issues/92) | Actief als overkoepelende planning. De oude hoofdfasenlijst beschreef niet meer welke openstaande werkzaamheden nog over zijn; dit document vervangt die lijst als uitvoeringsplan. |
| [#86 2Park-mock](https://github.com/VanHinthem/Parkeren/issues/86) | Grotendeels gerealiseerd. Mockserver en failure-injection bestaan; de issue meldt zelf dat de basis klaar is. Resterende contractdetails hangen af van #71. Rond af met een checklist-audit, geen nieuwe mock vanaf nul. |
| [#84 Transactionele Stop](https://github.com/VanHinthem/Parkeren/issues/84) | **Gesloten 4 oktober 2026.** Beide resterende criteria zijn getest: [Stop versus eindtijdwijziging](../../tests/Parkeren.IntegrationTests/Database/VisitSchedulerLockingTests.cs) en [pushfout na succesvolle Stop](../../tests/Parkeren.IntegrationTests/Database/PushDeliveryProcessorTests.cs). De twee betrokken testklassen slagen met 19/19 tests. |
| [#82 Domeinmodel/invarianten](https://github.com/VanHinthem/Parkeren/issues/82) | Open. De [criterium-naar-code/schema/test-matrix](issue-82-evidence-map.md) is bijgewerkt. PostgreSQL-tests bewijzen nu ook de genormaliseerde username- en kentekenconstraints; gerichte tests voor sessie-revocatie ontbreken nog, en providercriteria hangen deels aan #71. Volledige suite: 456/456 geslaagd op 4 oktober 2026. |
| [#78 PWA-capabilityvalidatie](https://github.com/VanHinthem/Parkeren/issues/78) | Actief. Android is gevalideerd; de iOS-matrix, offline herstel, meerdere devices/sessie-intrekking, subscription-refresh en productie-hostcheck staan nog open. Zie de [capabilitymatrix](fase-7-pwa-capability-matrix.md). |
| [#73 Visit-capaciteit](https://github.com/VanHinthem/Parkeren/issues/73) | Gedeeltelijk gerealiseerd. De lokale globale en per-user capaciteit wordt atomair geclaimd en getest. Providerlimiet, providerfout en resterende admin-/rapportagecriteria moeten met #71 worden afgerond. |
| [#71 2Park-gedrag](https://github.com/VanHinthem/Parkeren/issues/71) | Gedeeltelijk gevalideerd. Echte tests bevestigden action-ID/read-back, 240 minuten maximum, toekomstige `scheduled` acties en de vereiste seconde tussen opvolgers. De lokale foutafhandeling bewaart providercode en -bericht apart en zet providerresponses op `Unknown` met reconciliatie, zonder onbewezen foutcodemapping. Capaciteit, retries/onzekere responses en overige foutgevallen blijven gerichte vervolgvalidatie. Voer geen onnodige ketens van muterende acties uit. |
| [#68 2Park-storingen](https://github.com/VanHinthem/Parkeren/issues/68) | Gedeeltelijk gerealiseerd. Duurzame operations en unknown-outcome reconciliation bestaan. Getypeerde provider-responsefouten bij Start, continuation en Stop worden als `Unknown` opgeslagen met provider-code en reconciliatie; Stop wordt na zo'n fout niet definitief afgerond. De bredere foutclassificatie/backoff en functionele meldingen wanneer providerdekking tijdens gratis tijd nog niet bevestigd is, vragen nog verificatie en mogelijk implementatie. |
| [#64 Account/product-sync](https://github.com/VanHinthem/Parkeren/issues/64) | Gedeeltelijk gerealiseerd. Productcatalogus wordt bij iedere applicatiestart opnieuw gesynchroniseerd; een providerfout wordt gewaarschuwd en blokkeert startup niet. De laatste succesvolle balans, eenheid en ophaaltijd zijn per product persistent; één conditionele database-update voorkomt dat een oudere refresh een nieuwere snapshot overschrijft. Resterend: credentialvalidatie, periodieke refresh en veilige coördinatie met mutaties. |
| [#11 Deactiveren/archiveren](https://github.com/VanHinthem/Parkeren/issues/11) | **Gesloten 4 oktober 2026** na implementatie en review. API/UI ondersteunen veilig archiveren en verwijderen; PostgreSQL-tests bewijzen Visit-blokkade, statusconcurrency en historiebehoud. Commit `e2c1ee7`. |
| [#8 2Park-integratie-epic](https://github.com/VanHinthem/Parkeren/issues/8) | Relevant als parent van de nog open provider-sync-, storings- en capaciteitswerkzaamheden (#64, #68, #73) en de bijbehorende validatie. Geen apart implementatieproject naast de children. |
| [#6 Monitoring/notificaties-epic](https://github.com/VanHinthem/Parkeren/issues/6) | Relevant als parent; de issue meldt dat #78 de resterende child is. Sluit na afronding van die capability-spike. |
| [#1 Bezoekers/toegang-epic](https://github.com/VanHinthem/Parkeren/issues/1) | Parent van de bezoekers-/toegangswerkzaamheden; #11 is gesloten. Sluit pas na controle van de overige children. |

## Uitvoeringsvolgorde

De tracks hieronder kunnen naast elkaar lopen zodra hun expliciete afhankelijkheden zijn voldaan. GitHub blijft leidend voor status, eigenaar en voortgang.

### Gate 0 — Afsluiten wat al gebouwd is

De #82-matrix staat in [issue-82-evidence-map.md](issue-82-evidence-map.md). Vul de daar gemarkeerde testgaten aan en wacht voor providergebonden criteria op #71; sluit #82 pas wanneer elk criterium bewezen is of expliciet is overgedragen. #84 is gesloten nadat de aanvullende Stop-versus-eindtijd- en pushfailure-tests groen waren. Herhaal geen al bewezen provider- of mockimplementatie.

Voor #86 en het lokale deel van #73 geldt dezelfde audit, maar de definitieve acceptatie wacht op #71. Werk issuebeschrijvingen en checklists bij met concrete testnamen en bewijs. Alleen een ontbrekend criterium wordt een nieuwe taak.

**Exit:** ieder criterium van #82 is aantoonbaar afgedekt of heeft een concrete resterende taak; de issue wordt niet gesloten zolang bewijsleemtes of #71-afhankelijkheden onopgelost zijn. #84 is gesloten met testbewijs; de bestaande delen van #86/#73 zijn expliciet gemarkeerd als gerealiseerd.

### Provider- en recoverytrack

1. **Rond #71 gericht af.** Leg de al bevestigde 2Park-feiten vast en valideer alleen nog providerlimiet/capacity, retry en onzekere uitkomsten en foutresponses die nodig zijn voor #64, #68 en #73. Gebruik uitsluitend het testaccount, leg status/saldo vooraf vast en beëindig/reconcileer iedere muterende testactie.
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
- Sluit parent **#1** nadat #11 en de overige children van de epic zijn gecontroleerd.

**Exit:** import is herhaalbaar zonder duplicaten en sluit aan op het Oss-budget; gebruikers-/voertuighistorie blijft intact bij archivering/verwijdering.

## Release- en documentatiegates

- Werk na iedere slice de betreffende GitHub-checklist en functionele/technische documentatie bij.
- Voer gerichte tests uit tijdens de slice en de relevante volledige suites vóór merge; providerproeven blijven beperkt tot het expliciete testaccount.
- Behandel #82 als afrondwerk zolang de audit geen ontbrekend gedrag aantoont. #84 is gesloten met checklist- en testbewijs (19/19 gerichte tests).
- Sluit epics pas nadat hun children zijn afgerond. Sluit #92 als alle niet-epic implementatie-issues zijn afgehandeld en de resterende deployment/releasebesluiten expliciet zijn overgedragen.
