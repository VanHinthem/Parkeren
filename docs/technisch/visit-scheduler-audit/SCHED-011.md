# SCHED-011 — externe providerwijziging / discrepancy

**Status:** ✅ Audit afgerond; kernmechanisme goed, afhankelijk van providercontract  
**Afhankelijkheden:** SCHED-013, SCHED-016, SCHED-017

## Gewenste invariant

Wanneer providerstate buiten de applicatie verandert, mag de scheduler niet blind verder muteren alsof lokale state nog autoritatief is. Afwijkingen moeten zichtbaar worden en automatische continuation moet stoppen totdat de situatie veilig is.

## As-built gedrag

De worker voert periodiek `ReconcileActiveProviderActionsAsync` uit. Voor gezonde actieve Visits zonder unresolved provideroperation:

- worden lokale actieve actions vergeleken met de remote provider;
- missing action, statusverschil en eindtijdverschil worden als persistente discrepancy vastgelegd;
- een extern gestopte action wordt lokaal als zodanig gemarkeerd;
- de Visit gaat naar `AttentionRequired`;
- pending en claimed schedulerwork voor die Visit wordt geannuleerd;
- ook remote actions zonder lokale tegenhanger worden als discrepancy geregistreerd.

Dit is een conservatief en geschikt fail-safe model: bij onverwachte providerstate stopt automatische mutatie.

## Aandachtspunten

De juistheid van mismatchdetectie hangt af van de betekenis van providerstatussen en timestamps. Een natuurlijk geëindigde action mag niet ten onrechte als extern probleem worden geïnterpreteerd. Dat raakt:

- [SCHED-013](SCHED-013.md): normale Visit/action-afronding;
- [SCHED-016](SCHED-016.md): de mock simuleert tijdsverloop van statuses niet;
- [SCHED-017](SCHED-017.md): eindtijden worden op meerdere plekken bijna exact vergeleken terwijl live 2Park seconden kan afwijken.

## Positief

Periodieke reconciliation-fouten worden gelogd zonder de schedulerworker definitief te stoppen. Unresolved provideroperations worden bewust overgeslagen, zodat discrepancy-detectie niet concurreert met een lopende reconciliation.

## Onduidelijkheden / open vragen

1. Welke natuurlijke eindstatus geeft 2Park terug en hoe lang blijft een afgelopen action zichtbaar?
2. Welke timestampafwijking moet als echte discrepancy gelden in plaats van normale provider-normalisatie?
3. Moet een beheerder een `AttentionRequired` Visit handmatig kunnen reconciliëren/herstellen waarna schedulerwork opnieuw wordt opgebouwd?

## Conclusie

Geen zelfstandige architectuurfout gevonden in discrepancy-detectie. Het mechanisme is juist defensief; de resterende betrouwbaarheid hangt vooral af van een correct genormaliseerd providercontract.