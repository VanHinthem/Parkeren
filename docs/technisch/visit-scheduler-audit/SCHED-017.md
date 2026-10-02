# SCHED-017 — provider timestampmatching is strenger dan bevestigd 2Park-gedrag

**Status:** ✅ Opgelost via centrale provider matching policy  
**Prioriteit:** hoog

## Bevestigde observatie en besluit

Eerdere echte 2Park-tests lieten zien dat provider timestamps enkele seconden kunnen afwijken van lokaal aangevraagde waarden. Daarom gebruikt de applicatie geen exacte millisecondevergelijkingen meer als primaire provideridentiteit.

Voor V1 geldt centraal een **5 seconden provider timestamp tolerance** voor Start en End. Dit is bewust een **engineering margin**, geen gemeten of gedocumenteerde 2Park-SLA. Start en End gebruiken dezelfde waarde totdat later live bewijs een onderscheid rechtvaardigt.

## Huidig as-built matchingcontract

`ProviderActionMatchPolicy` centraliseert de matchingprioriteit:

1. bekende provider action-id;
2. provider productcontext;
3. genormaliseerd kenteken;
4. semantisch toegestane status;
5. Start/End binnen de centrale tolerance, voor zover de caller die timestamps kent.

Belangrijke veiligheidsregels:

- een bekende action-id mismatch valt nooit terug naar een andere kandidaat;
- zonder action-id mag fallback alleen exact één unieke kandidaat accepteren;
- locationcode versus providerlabel is geen harde identity mismatch;
- future Start/ContinueStart mag `scheduled` als geldige providerstatus accepteren;
- Stop blijft primair action-id driven en krijgt geen brede fallback-identificatie;
- `ExternalProviderAction` detection blijft exact provider-id gebaseerd en koppelt onbekende IDs niet automatisch aan lokale actions.

## Toepassing

De centrale semantiek wordt gebruikt voor:

- initiële Start read-back en reconciliation;
- continuation Start;
- Extend-precheck/read-back/reconciliation waar callercontext beschikbaar is;
- Stop identity/read-back zonder fallback;
- periodieke provider discrepancy-detectie;
- startup scheduler/recovery matching;
- scheduled wake-up en free-gap predecessorchecks;
- initial-coverage duplicate-prevention.

De oude `< 2 minuten`-heuristiek in `TwoParkProvider.StartActionAsync` is beoordeeld: deze bestond omdat de startresponse geen bruikbaar action-id opleverde en read-back een kandidaat moest zoeken. Die heuristiek is vervangen door dezelfde product/plate/status/5s unique-fallback policy.

## Regressiebewijs

Tests dekken onder meer:

- Start/End drift binnen de tolerance als match;
- +4 seconden End-drift als gezond in discrepancy-detectie;
- +6 seconden End-drift als echte mismatch;
- bekende-ID mismatch zonder fallback;
- onbekende-ID fallback met exact één kandidaat;
- ambigue fallback zonder automatische keuze;
- `scheduled` future actions;
- locationlabelverschil zonder identity mismatch;
- Unknown/reconciliation zonder blind duplicate mutation.

TwoParkMock kan de relevante timestampafwijkingen deterministisch injecteren.

## Bewust open providerpunt

De 5 seconden zijn geen provider-SLA. Wanneer toekomstige live observaties aantonen dat de veilige tolerance anders moet zijn, kan de centrale policy op één plek worden aangepast en via dezelfde regressiesuite worden gevalideerd.

## Conclusie

De eerdere milliseconde- en exacte timestampmatching is verwijderd uit de relevante identitypaden. Provideridentiteit is nu centraal, conservatief en tolerant voor de gekozen engineering margin, terwijl echte mismatches en ambiguïteit detecteerbaar blijven. Er resteert geen zelfstandig SCHED-017-gat.