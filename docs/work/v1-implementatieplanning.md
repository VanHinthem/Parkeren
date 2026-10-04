# Implementatieplan resterend V1-werk

**Reviewdatum:** 4 oktober 2026
**GitHub-planning:** [issue #92](https://github.com/VanHinthem/Parkeren/issues/92)
**Scope:** alle 15 openstaande issues op de reviewdatum.

## Uitkomst relevantiecheck

Geen van de 15 open issues is duidelijk achterhaald. Meerdere issues zijn wel gedeeltelijk gerealiseerd of vragen vooral nog een acceptatie-audit. Bouw die onderdelen niet opnieuw: werk de GitHub-checklists bij op basis van bestaand bewijs en maak alleen concrete ontbrekende punten tot implementatiewerk. Deze review sluit geen issues; afronding gebeurt nadat de genoemde gates zijn aangetoond.

| Issue | Huidige stand en resterende reden om open te blijven |
| --- | --- |
| [#94 PWA-hardening](https://github.com/VanHinthem/Parkeren/issues/94) | Actief. De service-worker-update is gehard, maar build-id, offline UX, manifest-/CI-controles en performancebudget zijn nog werk. `navigator.onLine` voorkomt nu alleen een updatecheck; het levert geen gebruikersstatus op. |
| [#93 Historie-import](https://github.com/VanHinthem/Parkeren/issues/93) | Actief. De 2Park-historie-reader bestaat, maar een idempotente import van oude transacties en opname in het Oss-budget is niet aangetroffen. |
| [#92 V1-plan](https://github.com/VanHinthem/Parkeren/issues/92) | Actief als overkoepelende planning. De oude hoofdfasenlijst beschreef niet meer welke openstaande werkzaamheden nog over zijn; dit document vervangt die lijst als uitvoeringsplan. |
| [#86 2Park-mock](https://github.com/VanHinthem/Parkeren/issues/86) | Grotendeels gerealiseerd. Mockserver en failure-injection bestaan; de issue meldt zelf dat de basis klaar is. Resterende contractdetails hangen af van #71. Rond af met een checklist-audit, geen nieuwe mock vanaf nul. |
| [#84 Transactionele Stop](https://github.com/VanHinthem/Parkeren/issues/84) | Closeout-kandidaat. De transactionele/idempotente stopclaim, reconciliation en concurrencytests bestaan. Verifieer nog de issuecriteria voor gelijktijdige eindtijdwijziging en notificatiefalen tegen de huidige tests en werk daarna de checklist bij. |
| [#82 Domeinmodel/invarianten](https://github.com/VanHinthem/Parkeren/issues/82) | Closeout-kandidaat. De domeinentiteiten, operationele lifecycle, unieke sleutels en concurrencybescherming zijn aanwezig. Maak eerst een criterium-naar-databaseconstraint/test-mapping; implementeer alleen eventuele echte hiaten. |
| [#78 PWA-capabilityvalidatie](https://github.com/VanHinthem/Parkeren/issues/78) | Actief. Android is gevalideerd; de iOS-matrix, offline herstel, meerdere devices/sessie-intrekking, subscription-refresh en productie-hostcheck staan nog open. Zie de [capabilitymatrix](fase-7-pwa-capability-matrix.md). |
| [#73 Visit-capaciteit](https://github.com/VanHinthem/Parkeren/issues/73) | Gedeeltelijk gerealiseerd. De lokale globale en per-user capaciteit wordt atomair geclaimd en getest. Providerlimiet, providerfout en resterende admin-/rapportagecriteria moeten met #71 worden afgerond. |
| [#71 2Park-gedrag](https://github.com/VanHinthem/Parkeren/issues/71) | Gedeeltelijk gevalideerd. Echte tests bevestigden action-ID/read-back, 240 minuten maximum, toekomstige `scheduled` acties en de vereiste seconde tussen opvolgers. De issue noemt capaciteit, retries/onzekere responses en foutgevallen nog als gerichte vervolgvalidatie. Voer geen onnodige ketens van muterende acties uit. |
| [#68 2Park-storingen](https://github.com/VanHinthem/Parkeren/issues/68) | Gedeeltelijk gerealiseerd. Duurzame operations, unknown-outcome reconciliation en notificatie-infrastructuur bestaan. De bredere foutclassificatie/backoff en functionele meldingen wanneer providerdekking tijdens gratis tijd nog niet bevestigd is, vragen nog verificatie en mogelijk implementatie. |
| [#64 Account/product-sync](https://github.com/VanHinthem/Parkeren/issues/64) | Gedeeltelijk gerealiseerd. Productcatalogus-sync en admin-providerstatus bestaan. Verifieer credentialvalidatie, blijvende freshness/`LastSuccessfulSyncAt`, periodieke refresh en coördinatie met mutaties; de huidige balancesnapshot-cache is proceslokaal. |
| [#11 Deactiveren/archiveren](https://github.com/VanHinthem/Parkeren/issues/11) | Actief. Activeren/deactiveren bestaat; een expliciete archiverings- en veilige permanente-verwijderflow voor records zonder historie ontbreekt in de API/UI. Historische relaties moeten behouden blijven. |
| [#8 2Park-integratie-epic](https://github.com/VanHinthem/Parkeren/issues/8) | Relevant als parent van de nog open provider-sync-, storings- en capaciteitswerkzaamheden (#64, #68, #73) en de bijbehorende validatie. Geen apart implementatieproject naast de children. |
| [#6 Monitoring/notificaties-epic](https://github.com/VanHinthem/Parkeren/issues/6) | Relevant als parent; de issue meldt dat #78 de resterende child is. Sluit na afronding van die capability-spike. |
| [#1 Bezoekers/toegang-epic](https://github.com/VanHinthem/Parkeren/issues/1) | Relevant als parent van het resterende archiverings-/deactiveringswerk (#11). Sluit na de child en controle van overige children. |

## Uitvoeringsvolgorde

De tracks hieronder kunnen naast elkaar lopen zodra hun expliciete afhankelijkheden zijn voldaan. GitHub blijft leidend voor status, eigenaar en voortgang.

### Gate 0 — Afsluiten wat al gebouwd is

Voer voor #82 en #84 een criterium-naar-code/databaseconstraint/test-mapping uit. Betrek bestaande PostgreSQL-tests voor capacity claims, stop/replay, Stop-versus-continuation, end-time-mutaties, recovery en pushdelivery. Herhaal geen al bewezen provider- of mockimplementatie.

Voor #86 en het lokale deel van #73 geldt dezelfde audit, maar de definitieve acceptatie wacht op #71. Werk issuebeschrijvingen en checklists bij met concrete testnamen en bewijs. Alleen een ontbrekend criterium wordt een nieuwe taak.

**Exit:** ieder criterium van #82 en #84 is aantoonbaar afgedekt of heeft een concrete resterende taak; de bestaande delen van #86/#73 zijn expliciet gemarkeerd als gerealiseerd.

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

- **#11:** implementeer archiveren als standaard voor records met historie; sta permanent verwijderen alleen toe zonder historische/actieve relaties. Blokkeer archiveren tijdens een actieve Visit en test behoud van rapportagehistorie.
- **#93:** bouw pas na bevestiging van providerdata en budgetsemantiek een idempotente import met expliciete herkomst en provider-ID. Importeer gerealiseerd providerverbruik zonder actuele parkeerregels over oude transacties heen te rekenen; test herhaalde sync, correcties en samenvoeging met nieuwe Visits.
- Sluit parent **#1** nadat #11 en de overige children van de epic zijn gecontroleerd.

**Exit:** import is herhaalbaar zonder duplicaten en sluit aan op het Oss-budget; gebruikers-/voertuighistorie blijft intact bij archivering/verwijdering.

## Release- en documentatiegates

- Werk na iedere slice de betreffende GitHub-checklist en functionele/technische documentatie bij.
- Voer gerichte tests uit tijdens de slice en de relevante volledige suites vóór merge; providerproeven blijven beperkt tot het expliciete testaccount.
- Behandel #82/#84 als afrondwerk zolang de audit geen ontbrekend gedrag aantoont; sluit ze pas met checklist- en testbewijs.
- Sluit epics pas nadat hun children zijn afgerond. Sluit #92 als alle niet-epic implementatie-issues zijn afgehandeld en de resterende deployment/releasebesluiten expliciet zijn overgedragen.
