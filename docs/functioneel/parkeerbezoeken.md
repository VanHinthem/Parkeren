# Parkeerbezoeken (Visits)

Een **Visit** is één logisch parkeerbezoek van een gebruiker met een toegewezen voertuig. Een Visit staat los van de onderliggende 2Park/provider-actions.

## V1 lifecycle na fase 5

De lifecycle gebruikt `Starting -> Active -> Stopping -> Completed`, met `Cancelled` voor een veilig afgebroken start. Operationele gezondheid/reconciliation staat los van de lifecycle.

Een gebruiker kan via het dashboard:
- de actuele Visit bekijken;
- een Visit starten voor een toegewezen voertuig;
- een gewenste eindtijd kiezen binnen de geladen effectieve policy;
- een actieve Visit handmatig stoppen wanneer de policy dit toestaat;
- de gewenste eindtijd van een actieve Visit verlengen;
- recente en afgeronde Visits bekijken.

## Autoritatieve validatie

De UI beperkt keuzes vooraf, maar de backend blijft autoritatief voor voertuigautorisatie, policy, parkeerregels, capaciteit en providerresultaat. Een mislukte of onzekere provideruitkomst wordt niet als succesvol gepresenteerd.

## Capaciteit

`MaxConcurrentVisits` komt uit de operationele context. Actieve lifecycle-states bezetten capaciteit totdat een Visit definitief is beëindigd. De UI toont de server-side bezetting en blokkeert starten wanneer capaciteit onbekend of vol is.

## Reconciliation

Bij een onzekere Start/Stop-uitkomst blijft de Visit in een niet-definitieve toestand. De client ververst de status periodiek en toont dat providerbevestiging nog loopt. Fouten bij het vernieuwen worden zichtbaar gemaakt zonder de polling te stoppen.

## Grenzen van fase 5

Fase 5 levert de complete interactieve Visit-flow tegen de mock/provideradapter. Persistente scheduler-work, just-in-time continuation, restart recovery, worker-races en uitgebreide unknown-outcome/externe-provider reconciliation horen bij fase 6. Echte 2Park-semantiek wordt in fase 9 gevalideerd.
