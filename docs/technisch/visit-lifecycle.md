# Visit lifecycle — technische implementatie

## Fase 5 baseline

Fase 5 verbindt Domain, Application, Infrastructure, API en React UI tot een complete Visit-flow tegen de 2Park-mock.

### Persistente kern

- `Visit` bewaart eigenaar, voertuig, start/eindtijden, lifecycle en health.
- De effectieve parking policy wordt bij start als immutable snapshot gebruikt voor de Visit.
- `ProviderParkingAction` koppelt providerwerk aan de Visit.
- `ProviderOperation` geeft provider-mutaties een stabiele operation/idempotency-context.
- Capacity wordt server-side en concurrency-safe geclaimd.

### Application flows

De applicatielaag bevat afzonderlijke flows voor:
- Start Visit;
- Stop Visit;
- wijzigen van `DesiredEndAt`;
- provider start/read-back/reconciliation;
- policy/rules/capacity-resolutie.

De backend valideert mutaties opnieuw; frontendvalidatie is uitsluitend UX.

### HTTP/API

De webclient gebruikt authenticated endpoints voor:
- actieve Visit;
- starten;
- stoppen;
- eindtijd wijzigen;
- recente Visits;
- Visit-historie;
- capaciteit;
- effectieve parking policy.

Provider-onzekerheid kan als reconciliation-resultaat terugkomen. De UI houdt de Visit dan zichtbaar en pollt de actuele state.

### UI

Het dashboard toont de actieve Visit, verstreken tijd, gewenste eindtijd, Stop/Verleng-acties, capaciteit en recente acties. Zonder actieve Visit wordt de Start-flow getoond met voertuig- en duurkeuze. Policy/capacity moeten bekend zijn voordat een mutatie beschikbaar wordt.

### Fasegrens

Fase 5 implementeert geen persistente scheduler/worker. Just-in-time continuation, restart recovery, obsolete-work invalidatie, worker concurrency en bredere reconciliation worden in fase 6 gebouwd. Providergedrag dat alleen tegen echt 2Park bewezen kan worden blijft gekoppeld aan spike #71/fase 9.
