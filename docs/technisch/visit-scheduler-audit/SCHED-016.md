# SCHED-016 — TwoParkMock modelleert natuurlijke tijdsstatussen niet

**Status:** ✅ Testharness gehard; provider-onbekenden blijven expliciet configureerbaar  
**Prioriteit:** middel/hoog

## Gewenste invariant

Scheduler-boundarytests moeten deterministisch tijd kunnen sturen en relevante providerafwijkingen kunnen simuleren zonder onbewezen live 2Park-gedrag als default in de mock vast te leggen.

## Huidig as-built gedrag

TwoParkMock heeft inmiddels één centrale bestuurbare `MockClock` met testendpoints voor set, advance en reset. Daardoor zijn wall-clock sleeps uit schedulerboundarytests niet nodig.

Provider-readback leidt status dynamisch af:

- vóór Start -> `scheduled`;
- vanaf Start tot End -> `active`;
- expliciet `stopped` blijft terminal en wordt nooit door tijd overschreven.

Voor gedrag ná End wordt bewust geen live-providerdefault verzonnen. Tests kunnen expliciet kiezen uit:

- `keep-active`;
- `completed`;
- `hide`.

Zonder expliciete modus blijft de bestaande ongespecificeerde semantiek behouden totdat live 2Park-bewijs beschikbaar is.

De mock ondersteunt daarnaast deterministische fault/read-back injection voor:

- visibility delay op de mockklok;
- Start- en End-timestamp offsets;
- afwijkend read-back locationlabel zonder opgeslagen mutation-intent te veranderen;
- unknown-after-write en bestaande mutation failure modes;
- scheduled-capacity wel/niet laten meetellen;
- capaciteit op basis van afgeleide actuele providerstate, zodat een scheduled action na klokadvance vanaf Start als actief meetelt.

`/api/test/reset` herstelt de klok en alle testoverrides naar hun defaults.

## Regressiebewijs

De huidige tests bewijzen onder meer:

- klok set/advance/reset;
- `scheduled -> active` exact op Start;
- gestopte scheduled action blijft gestopt;
- visibility delay volgt alleen de mockklok;
- read-back timestamp offsets veranderen mutation-intent niet en resetten correct;
- location code versus label kan afzonderlijk worden gesimuleerd;
- alle expliciete post-End modi en Stop-precedence;
- scheduled-capacitymodus en derived active capacity;
- JIT successor wordt remote actief zonder duplicate successor bij redundant schedulerwork;
- overnight/free-gap en recovery kunnen zonder minutenlange waits worden getest.

## Bewust open providercontract

Nog niet hard gemeten bij echt 2Park:

- natuurlijke post-End status/visibility;
- of een toekomstige `scheduled` action al vóór Start providercapaciteit verbruikt;
- een echte timestamp-SLA.

Deze punten blijven daarom configureerbare testdimensies en worden niet als providerfeit in productielogica ingebakken.

## Conclusie

Het oorspronkelijke statische tijdsmodel is vervangen door een deterministische scheduler-testharness met dynamische tijdsstatussen en gerichte fault injection. De bekende live-contractonzekerheden blijven expliciet configureerbaar. Er resteert geen zelfstandig SCHED-016 harness-gat.