# SCHED-012 — Long Visit warning schedulergedrag

**Status:** ✅ Opnieuw geverifieerd na work-type execution policy  
**Prioriteit:** middel  
**Relatie:** SCHED-015

## Gewenste invariant

Een Long Visit warning gaat over de duur van een nog actieve Visit. Een tijdelijke technische healthstatus van die Visit hoort niet zonder expliciete productkeuze een reeds geplande waarschuwing permanent te verwijderen.

## Huidig as-built gedrag

Bij het activeren van een Visit maakt `VisitStartStore` een `LongVisitWarning` workitem wanneer `LongVisitWarningAfter` is ingesteld. De processor:

- controleert dat de Visit nog `Active` is;
- schrijft notification event en inboxmelding;
- betrekt de actuele admin-notificatie-instelling;
- plant optioneel een volgende reminder op basis van het actuele reminderinterval.

De generieke `Active + Healthy` claim-gate uit de oorspronkelijke audit bestaat niet meer. `VisitSchedulerWorkExecutionPolicy` behandelt elk work-type apart.

Voor `LongVisitWarning` geldt nu expliciet:

- `Starting` → `Defer`;
- `Active` → `Execute`;
- `Stopping`, `Completed`, `Cancelled` → `Cancel`.

Die beslissing is onafhankelijk van `VisitHealth`. Een actieve Visit in `Reconciling`, `AttentionRequired` of `StopFailed` verliest de waarschuwing dus niet alleen door zijn technische healthstatus.

## Regressiebewijs

De huidige tests bewijzen onder meer:

- de volledige Long Visit Warning lifecycle-matrix is voor alle healthwaarden gelijk;
- een actieve Visit voert warning-work uit ongeacht health;
- een niet-actieve terminale/stopping Visit krijgt geen nieuwe warning;
- de initiële warning wordt persistent gepland;
- visitor- en optionele admin-notificaties worden aangemaakt volgens de instellingen;
- een reminder wordt alleen gepland wanneer een reminderinterval is ingesteld;
- Stop claim annuleert pending Long Visit warning-work.

Runtime claiming en failed-work recovery gebruiken dezelfde centrale work execution policy, zodat het gedrag niet alleen in de processor maar ook rond claim/retry consistent blijft.

## Relaties

- [SCHED-015](SCHED-015.md): centrale work-type execution policy loste de generieke health-gating op.
- [SCHED-003](SCHED-003.md): Stop blijft leidend; bij `Stopping` wordt warning-work geannuleerd.

## Conclusie

De oorspronkelijke bevinding is opgelost. Long Visit warning-work volgt nu de functionele Visit-lifecycle in plaats van een generieke health-gate. Tijdelijke technische healthstatussen verwijderen een geplande warning niet permanent. Er resteert geen zelfstandig SCHED-012-gat.