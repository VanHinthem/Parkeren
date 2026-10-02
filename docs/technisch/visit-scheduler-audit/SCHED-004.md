# SCHED-004 — `DesiredEndAt` verkorten

**Status:** ⚠️ Audit afgerond; afhankelijk van gedeelde bevindingen  
**Prioriteit:** hoog vanwege deferred stop  
**Scenario:** een actieve Visit wordt verkort naar een toekomstig tijdstip.

## Gewenste invariant

Alle providerdekking na de nieuwe eindtijd moet verdwijnen. Een actieve provideraction die niet kan worden ingekort moet duurzaam op de nieuwe Visit-eindtijd worden gestopt. Scheduled successors moeten vooraf worden geannuleerd of vervangen.

## As-built gedrag

De flow is bewust in twee fasen opgezet:

1. `PrepareAsync` legt de end-time change duurzaam vast.
2. `VisitEndTimeProviderAdjuster` beoordeelt bestaande `Active`/`Scheduled` provideractions.
3. Scheduled actions worden zo nodig via een duurzame child `ProviderOperation` geannuleerd of vervangen.
4. Timeout/network/read-back-onzekerheid wordt `Unknown` en blokkeert Apply totdat recovery duidelijkheid geeft.
5. `ApplyAsync` wijzigt pas daarna de `DesiredEndAt`, annuleert obsolete pending work en plant indien nodig `StopVisit` op exact de nieuwe eindtijd.

Voor `ShortenActive` wordt de actieve provideraction niet kunstmatig verlengd/ingekort via de provider; de applicatie gebruikt terecht een deferred `StopVisit`.

## Positieve testdekking

Er zijn tests voor:

- atomair annuleren van toekomstig schedulerwork;
- classifiergedrag voor active/scheduled actions;
- cancel/replace van scheduled actions;
- durable scheduled stop;
- recovery van onzekere child operations.

## Gedeelde risico's

- [SCHED-014](SCHED-014.md): end-time change neemt eerst de Visit advisory lock, terwijl schedulerclaim eerst een work-row lock neemt. Dit vormt een lock-order inversion.
- [SCHED-015](SCHED-015.md): het deferred `StopVisit` kan door de generieke claimer worden geannuleerd wanneer de Visit op het uitvoermoment niet `Healthy` is. Juist bij een probleemstatus blijft stoppen echter veiligheidskritisch.
- [SCHED-001](SCHED-001.md): cancel/replace-logica wordt belangrijker zodra continuation correct vooraf scheduled successors maakt.

## Onduidelijkheden / open vragen

1. Moet een deferred `StopVisit` ook uitgevoerd worden wanneer de Visit `Reconciling` of `AttentionRequired` is? De audit gaat ervan uit dat dit functioneel gewenst is, omdat providerdekking anders voorbij de expliciete nieuwe eindtijd kan blijven bestaan.
2. Wat is het gewenste gedrag als scheduled-cancel definitief niet kan worden gereconcilieerd vóór de nieuwe eindtijd?

## Conclusie

Het shorten-ontwerp is inhoudelijk sterk en durable. Geen aparte herbouw is noodzakelijk, maar de betrouwbaarheid hangt direct af van work-type-specifieke gating en consistente locking.