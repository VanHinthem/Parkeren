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

Fase 6 gebruikt persistente scheduler-work: de lokale controle kan vijf minuten vóór het einde van providerdekking plaatsvinden, maar de daadwerkelijke vervolgactie start pas op de grens. Na een gratis periode wordt vervolgwerk op het volgende betaalde begin gepland. Bij opstarten herstelt de server onbekende provideruitkomsten en ontbrekend schedulerwerk. Iedere minuut probeert de worker nog onbekende provideruitkomsten opnieuw te reconciliëren zonder lopende workerclaims vrij te geven. Daarna vergelijkt hij bekende actieve provideracties met de provider; een externe stop of afwijkende status/eindtijd blokkeert verdere automatische acties en vraagt aandacht. Providerstoringen tijdens deze periodieke controle blokkeren de schedulerloop niet. Providergedrag dat alleen tegen echt 2Park bewezen kan worden blijft gekoppeld aan spike #71/fase 9.

Voor een gratis gestarte Visit met concrete eindtijd wordt het begin van het eerstvolgende betaalde segment als persistente scheduler-work opgeslagen. De worker maakt op die grens pas de eerste provideractie aan. Een gratis interval tussen betaalde provideracties krijgt eveneens werk op de volgende betaalgrens; de verlopen actie wordt afgerond. Bij een onzekere providerstart blijft de operation bewaard voor reconciliation. Het open-ended pad (`DesiredEndAt = null`) en validatie van werkelijk 2Park-grensgedrag blijven aparte vervolgstappen.

Ook een Visit die tijdens betaalde tijd begint, begrenst zijn eerste provideractie op het einde van het actuele betaalde segment (of de kortere providerduur). Als daarna een gratis interval volgt, wordt vervolgwerk op de volgende betaalgrens gezet; zonder volgend betaald segment is geen providervervolg nodig.

## Lifecycle-aanscherping na scheduler-audit — oktober 2026

De betrouwbaarheidsaudit heeft een ontbrekende verantwoordelijkheid zichtbaar gemaakt: het beëindigen van providerdekking is niet automatisch hetzelfde als het beëindigen van de logische Visit.

Voor V1 geldt daarom expliciet:

> Providerdekking en Visit-finalization zijn gescheiden verantwoordelijkheden.

### Effectieve terminale Visitgrens

Een actieve Visit eindigt op de vroegste toepasselijke harde grens uit:

1. `DesiredEndAt`, indien aanwezig;
2. `StartAt + MaxVisitElapsedDuration`, indien aanwezig;
3. de wall-clock boundary waarop `MaxPaidParkingDuration` is verbruikt, indien aanwezig.

`MaxPaidParkingDuration` en `MaxVisitElapsedDuration` zijn harde Visit-grenzen. Ze stoppen dus niet alleen verdere providerdekking, maar beëindigen de hele Visit.

Een volledig open-ended Visit zonder deze grenzen heeft geen vooraf bekende natuurlijke eindtijd.

### Uniforme terminale lifecycle

Zowel handmatig als automatisch eindigen gebruikt dezelfde lifecycle:

```text
Active
-> Stopping
-> Completed
```

De oorzaak verschilt, maar er komt geen aparte status voor een automatisch afgelopen Visit.

Minimaal moeten de volgende eindredenen functioneel herleidbaar blijven:

- handmatige stop;
- `DesiredEndAt` bereikt;
- `MaxVisitElapsedDuration` bereikt;
- `MaxPaidParkingDuration` bereikt.

### Durable terminal work

Zodra een effectieve terminale grens berekenbaar is, moet durable scheduler-work bestaan dat de Visit op die grens via de normale Stop/finalization-orchestration afhandelt.

Dit geldt ook voor:

- volledig gratis Visits;
- Visits zonder enige provideraction;
- Visits waarvan de laatste provideraction exact op de Visitgrens eindigt;
- Visits met een gratis staart na de laatste betaalde provideraction.

Continuation-work blijft uitsluitend verantwoordelijk voor providerdekking vóór de terminale Visitgrens.

### Finalization-volgorde

Op de terminale grens geldt functioneel:

```text
Visit opnieuw valideren
-> Active naar Stopping
-> actieve/scheduled/unknown provideractions veilig afhandelen
-> provider-onzekerheid zo nodig reconciliëren
-> pas zonder open providerwerk:
   Visit naar Completed
```

Een provideraction die reeds natuurlijk is geëindigd hoeft niet onnodig gestopt te worden, maar de lokale state moet eerst betrouwbaar terminal zijn bevestigd.

Een scheduled successor mag de Visitgrens nooit overleven.

### `ActualEndAt`

Bij een natuurlijk gepland einde is `Visit.ActualEndAt` de functionele effectieve terminale Visitgrens.

Providerreadback-timestamps mogen daarvan enkele seconden afwijken en bepalen daarom niet achteraf de Visit-eindtijd. Zij worden gebruikt om provideraction-state te bevestigen/reconciliëren.

Bij een handmatige stop blijft `ActualEndAt` het daadwerkelijke stopmoment.

### Wijzigen van de eindtijd

Bij een wijziging van `DesiredEndAt` wordt de effectieve terminale grens opnieuw bepaald.

- Verkorten vervangt obsolete terminal work, annuleert continuation na de nieuwe grens en handelt providerdekking voorbij die grens durable af.
- Verlengen verschuift terminal work alleen wanneer `DesiredEndAt` werkelijk de leidende grens is; een eerdere policygrens blijft leidend.

### Recovery

Startup recovery moet voor iedere `Active` Visit met een berekenbare terminale grens ook kunnen reconstrueren dat precies één relevante terminale taak bestaat.

Is de grens tijdens downtime al verstreken, dan wordt de taak direct uitvoerbaar en wordt dezelfde Stop/finalization-flow gebruikt.

### Schedulerprioriteit

Op of na de terminale Visitgrens mag geen continuation of Long Visit reminder meer worden gestart. Wanneer meerdere scheduler-items exact dezelfde `DueAt` hebben, heeft terminale finalization functioneel voorrang.

De technische lock- en claimvolgorde wordt verder uitgewerkt bij SCHED-014 en SCHED-015.

Zie `docs/technisch/visit-scheduler-audit/SCHED-013.md` voor de volledige auditbevinding, ontwerpbesluiten en verificatiecriteria.