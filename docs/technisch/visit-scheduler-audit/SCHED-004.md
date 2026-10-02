# SCHED-004 — `DesiredEndAt` verkorten

**Status:** ✅ Opnieuw geverifieerd na scheduler-hardening  
**Prioriteit:** hoog vanwege deferred stop  
**Scenario:** een actieve Visit wordt verkort naar een toekomstig tijdstip.

## Gewenste invariant

Alle providerdekking na de nieuwe eindtijd moet verdwijnen. Een actieve provideraction die niet kan worden ingekort moet duurzaam op de nieuwe Visit-eindtijd worden gestopt. Scheduled successors moeten vooraf worden geannuleerd of vervangen.

## Huidig as-built gedrag

De flow blijft bewust in twee fasen opgezet:

1. `PrepareAsync` legt de end-time change duurzaam vast.
2. `VisitEndTimeProviderAdjuster` beoordeelt bestaande `Active`/`Scheduled` provideractions.
3. Scheduled actions worden zo nodig via een duurzame child `ProviderOperation` geannuleerd of vervangen.
4. Timeout/network/read-back-onzekerheid wordt `Unknown` en blokkeert Apply totdat recovery duidelijkheid geeft.
5. `ApplyAsync` wijzigt pas daarna de `DesiredEndAt`, annuleert obsolete pending work en plant de actuele terminale `StopVisit` via dezelfde centrale terminal-work planning.

Voor `ShortenActive` wordt de actieve provideraction niet kunstmatig via provider-extensionlogica ingekort. De applicatie bewaakt de nieuwe functionele Visit-grens met durable terminal `StopVisit` work.

De relevante gedeelde schedulerinvarianten zijn inmiddels gehard:

- schedulerclaim en end-time change gebruiken dezelfde Visit-first lock-order: Visit advisory lock vóór scheduler/provider rows;
- `StopVisit` gebruikt de centrale work execution policy en blijft uitvoerbaar bij tijdelijke probleem-health zoals `Reconciling` en `AttentionRequired`;
- terminal work heeft prioriteit boven continuation bij gelijke `DueAt`;
- continuation maakt future scheduled successors vooraf aan, maar de end-time change flow behandelt die expliciet via cancel/replace in plaats van ze te negeren;
- unresolved provideroperations worden bij startup/recovery eerst gereconcilieerd en nooit blind opnieuw gemuteerd.

## Positieve testdekking

De huidige testset dekt onder meer:

- atomair annuleren van toekomstig schedulerwork;
- classifiergedrag voor active/scheduled actions;
- cancel/replace van scheduled actions;
- een replacement action die `Scheduled` blijft en exact op de verkorte eindgrens wordt begrensd;
- durable scheduled/terminal stop van een actieve provideraction;
- recovery van onzekere child operations;
- Visit-first schedulerclaim versus end-time change zonder lock-order inversion;
- work-policygedrag waardoor veiligheidskritisch terminal Stop-work niet door afwijkende Visit-health verloren gaat.

## Herbeoordeling eerdere risico's

De eerdere gedeelde risico's zijn niet meer open:

- **SCHED-014:** opgelost door uniforme Visit-first locking en PostgreSQL-racetests.
- **SCHED-015:** opgelost door work-type-specifieke execution policy; terminal Stop wordt niet meer generiek door `Active + Healthy` gating geannuleerd.
- **SCHED-001:** future scheduled successors zijn geïmplementeerd en de shortening-flow heeft expliciete cancel/replace- en recoverysemantiek voor deze actions.

Wanneer een scheduled-cancel tijdelijk niet eenduidig kan worden gereconcilieerd, blijft de provideroperation unresolved en wordt geen nieuwe mutation blind uitgevoerd. De terminale Stop-intent blijft onafhankelijk durable en mag door `AttentionRequired` niet worden weggefilterd; afronding van de Visit blijft wachten totdat provideractions veilig terminal zijn.

## Conclusie

De shortening-flow voldoet na de scheduler-hardening aan de gewenste invariant. `DesiredEndAt` wordt pas toegepast nadat noodzakelijke provideradjustments veilig zijn voorbereid/reconciled, scheduled successors worden expliciet behandeld en de nieuwe terminale grens blijft durable afdwingbaar. Er resteert geen zelfstandige SCHED-004-fix.