# SCHED-009 — provider timeout / unknown continuation

**Status:** ⚠️ Audit afgerond; recoverymodel sterk, provider-matching te strikt  
**Prioriteit:** hoog via SCHED-017

## Gewenste invariant

Na een timeout, netwerkfout of onduidelijke providerresponse mag dezelfde mutation niet blind opnieuw worden uitgevoerd. Eerst moet via read-back/reconciliation worden vastgesteld of de eerdere provideractie wel of niet heeft plaatsgevonden.

## As-built gedrag

De provider-start executor behandelt `Unknown` en `Reconciling` expliciet. Een persistente `ProviderOperation` en `ProviderParkingAction` bestaan vóór de externe mutation. Bij replay van een `InProgress` attempt geldt bovendien een vijf-minuten lease; een nog geldige attempt wordt niet overgenomen en een stale attempt wordt eerst naar `Unknown` gebracht.

De reconciler zoekt daarna de action terug via provider action-id indien die al bekend is, anders via kenteken/starttijd. Pas wanneer de providerstate voldoende eenduidig is wordt de operation bevestigd. Bij geen match zonder provider-id kan de operation retryable worden gemaakt.

Dit is de juiste fundamentele idempotencystrategie: persist first, mutate, read back, reconcile before retry.

## Bevinding

De betrouwbaarheid wordt ondermijnd door de criteria waarmee een providerresponse als dezelfde action wordt herkend. De directe read-back in `StartVisitProviderExecutor` eist exacte start- en eindtimestamps en status `active`. De reconciler gebruikt slechts minder dan 1 ms timestamp-tolerantie.

De live 2Park-test uit #71 heeft juist vastgelegd dat provider timestamps enkele seconden van de lokaal aangevraagde waarden kunnen afwijken. Daardoor kan een daadwerkelijk succesvolle mutation onnodig `Unknown` blijven. Zie [SCHED-017](SCHED-017.md).

Voor toekomstige scheduled continuation geldt bovendien dat de directe executor alleen `active` accepteert, terwijl de provider bij een toekomstige start terecht `scheduled` retourneert; dit raakt [SCHED-001](SCHED-001.md).

## Testdekking

Er zijn tests voor unknown outcomes, retry/replay, stale in-progress attempts en recovery. De fout zit niet in het ontbreken van een recoverymodel, maar in de te strikte provider-identificatiecriteria.

## Onduidelijkheden / open vragen

1. Welke velden zijn bij 2Park voldoende stabiel om een mutation uniek terug te vinden: provider action-id, kenteken, geplande startwindow, product en locatie?
2. Welke timestamp-tolerantie is veilig zonder twee verschillende actions voor hetzelfde kenteken te verwarren?
3. Is de provider action-id altijd beschikbaar in een response die client-side alsnog als timeout eindigt?

## Conclusie

Het durable unknown/reconciliationmodel moet behouden blijven. De eerstvolgende ontwerpstap is niet een andere retrystrategie, maar robuuste provider-matching op basis van het live contract.