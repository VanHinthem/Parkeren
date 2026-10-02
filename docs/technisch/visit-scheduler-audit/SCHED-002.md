# SCHED-002 — gratis periode / overnight naar volgende betaalde periode

**Status:** ✅ Geïmplementeerd en regressiegeverifieerd  
**Prioriteit:** hoog  
**Scenario:** een Visit loopt over gratis tijd en heeft daarna opnieuw betaalde providerdekking nodig.

## Gewenste invariant

Tijdens gratis tijd bestaat geen providerdekking. Vóór het volgende betaalde segment moet de benodigde provideraction wel tijdig klaarstaan:

```text
precheck        = nextPaid.Start - 5 minuten
successor.Start = nextPaid.Start
```

De Visit blijft gedurende het gratis interval logisch actief.

## Huidig as-built gedrag

Alle relevante planningspaden gebruiken dezelfde free-gap semantiek:

- Visit start tijdens gratis tijd;
- resultaat van een eerste providerstart;
- continuation-resultaat;
- extend-resultaat voor generieke providers;
- end-time extension;
- startup recovery.

Wanneer het volgende betaalde segment pas later begint, wordt `ContinueProviderCoverage` op T-5 van `nextPaid.Start` gepland. De future action kan dan als `scheduled` bij de provider bestaan vóór het betaalde segment werkelijk begint.

Over een echt gratis gat wordt geen `+1 seconde` toegepast; de nieuwe action begint exact op `nextPaid.Start`.

## Recovery en duplicate-prevention

Startup recovery herkent reeds bestaande future scheduled coverage en maakt die niet opnieuw aan. De Visit blijft actief zonder providerdekking zolang het segment gratis is.

## Provider post-End gedrag

Het exacte live 2Park-gedrag nadat een predecessor natuurlijk over `End` heen is — bijvoorbeeld `completed`, verborgen of tijdelijk nog zichtbaar — is niet hard gemeten.

TwoParkMock legt daarom geen onbewezen default vast. Tests kunnen expliciet `keep-active`, `completed` of `hide` kiezen. De applicatiecorrectness leunt niet op één specifieke post-End providerstatus.

## Regressiebewijs

De hardening bewijst:

- T-5 planning voor toekomstige betaalde segmenten;
- geen providercoverage tijdens gratis tijd;
- future scheduled successor met start exact op `nextPaid.Start`;
- restart/recovery zonder duplicate successor;
- persisted planning over versioned rulesets;
- provider matching met centrale 5-seconden engineering tolerance.

De free-gap acceptance/recoveryketen is groen t/m `6cc9bf05`.

## Conclusie

De oorspronkelijke grens-latency en remote-state-aanname zijn uit de planning verwijderd. SCHED-002 heeft geen zelfstandig code- of testgat meer. Alleen het exacte live 2Park post-End contract blijft een extern observatiepunt.
