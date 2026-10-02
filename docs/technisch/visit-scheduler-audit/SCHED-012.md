# SCHED-012 — Long Visit warning schedulergedrag

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** middel  
**Relatie:** SCHED-015

## Gewenste invariant

Een Long Visit warning gaat over de duur van een nog actieve Visit. Een tijdelijke technische healthstatus van die Visit hoort niet zonder expliciete productkeuze een reeds geplande waarschuwing permanent te verwijderen.

## As-built gedrag

Bij het activeren van een Visit maakt `VisitStartStore` een `LongVisitWarning` workitem wanneer `LongVisitWarningAfter` is ingesteld. De processor:

- controleert dat de Visit nog `Active` is;
- schrijft een notification event en inboxmelding;
- betrekt de actuele admin-notificatie-instelling;
- plant optioneel een volgende reminder op basis van de actuele reminderinterval.

Integratietests bewijzen zowel de initiële warning/reminder als het ontbreken van een reminder wanneer geen interval is geconfigureerd.

## Bevinding

De processor zelf verlangt alleen `VisitStatus.Active`, maar het work bereikt de processor alleen via `PostgresVisitSchedulerWorkClaimer`. Die claimer vereist voor **ieder** work-type `Active + Healthy`.

Een actieve Visit die op het warningmoment bijvoorbeeld `Reconciling` of `AttentionRequired` is, krijgt het warning-work daarom niet geclaimd maar `Cancelled`. De waarschuwing en eventuele reminderreeks verdwijnen daarmee definitief.

Dit is een concrete manifestatie van [SCHED-015](SCHED-015.md): work-types hebben verschillende veiligheidssemantiek maar worden generiek gegated.

## Onduidelijkheden / open vragen

1. Willen we functioneel een Long Visit warning voor iedere `Active` Visit, onafhankelijk van health? Dat lijkt het meest logisch, maar moet expliciet bevestigd worden.
2. Als `AttentionRequired` al een andere notificatie heeft veroorzaakt, willen we Long Visit warnings daarnaast blijven sturen of tijdelijk onderdrukken?
3. Moeten reminder-workitems bij een Visit die naar `Stopping` gaat direct worden gecanceld? Dat gedrag is logisch en bestaat via stopclaim voor pending work.

## Conclusie

De notificationlogica zelf is goed getest. De generieke schedulerclaim-gate maakt het gedrag echter afhankelijk van een niet-functionele healthstatus. Dit hoort in het gedeelde work-type gating-ontwerp opgelost te worden.