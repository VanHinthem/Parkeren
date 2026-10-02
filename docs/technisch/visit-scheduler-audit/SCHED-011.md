# SCHED-011 — externe providerwijziging / discrepancy

**Status:** ✅ Opnieuw geverifieerd na matching-, lifecycle- en mock-hardening  
**Afhankelijkheden:** SCHED-013, SCHED-016, SCHED-017

## Gewenste invariant

Wanneer providerstate buiten de applicatie verandert, mag de scheduler niet blind verder muteren alsof lokale state nog autoritatief is. Afwijkingen moeten zichtbaar worden en automatische continuation moet stoppen totdat de situatie veilig is.

## Huidig as-built gedrag

`VisitRecoveryService.ReconcileActiveProviderActionsAsync` vergelijkt gezonde actieve Visits zonder unresolved provideroperation periodiek met provider-readback. Daarbij gelden de centrale provideridentity- en timestampregels uit SCHED-017:

- bekende provider action-id en productcontext zijn leidend;
- locationlabel versus lokale locationcode is geen identity mismatch;
- End-drift binnen de centrale 5-seconden engineering margin is gezond;
- drift buiten die tolerance wordt als discrepancy vastgelegd;
- een extern gestopte action wordt lokaal als gestopt gemarkeerd;
- de Visit gaat bij een echte unresolved afwijking naar `AttentionRequired`;
- continuation wordt via de work execution policy niet verder uitgevoerd zolang de Visit niet gezond is;
- provider-only actions blijven als persistente discrepancy zichtbaar totdat de provider ze als gestopt rapporteert.

Unresolved provideroperations worden bewust niet door de periodieke discrepancycheck doorkruist; eerst wordt de mutation zelf gereconcilieerd.

## Regressiebewijs

De huidige integratietests bewijzen onder meer:

- een externe Stop wordt als statusdiscrepancy gedetecteerd en de lokale provideraction wordt `Stopped`;
- een provider-only action blijft open totdat de provider hem als gestopt rapporteert;
- End-drift van +4 seconden veroorzaakt geen discrepancy en laat de Visit `Healthy`;
- End-drift van +6 seconden veroorzaakt `ProviderActionEndMismatch` en `AttentionRequired`;
- startup/restart blokkeert continuation na externe Stop of extern gewijzigde provider-End;
- de centrale scheduler work policy deferreert continuation voor `Reconciling`, `AttentionRequired` en `StopFailed` in plaats van verder te muteren.

## Providercontract dat bewust nog open blijft

Het exacte live 2Park-gedrag ná het natuurlijke End van een action — bijvoorbeeld statusnaam en zichtbaarheidstijd — is nog niet hard gemeten. TwoParkMock legt daarom geen onbewezen default vast: tests kunnen expliciet `keep-active`, `completed` of `hide` kiezen.

Dit externe observatiepunt verandert de fail-safe applicatiesemantiek niet. Tot live bewijs beschikbaar is, blijven bekende IDs, centrale tolerance en conservatieve discrepancy/Unknown-afhandeling leidend.

## Relaties

- [SCHED-013](SCHED-013.md): normale terminale Visit-afronding is inmiddels duurzaam opgelost.
- [SCHED-016](SCHED-016.md): mock ondersteunt expliciete post-End modi, kloksturing en read-back afwijkingen.
- [SCHED-017](SCHED-017.md): centrale provideridentity en 5-seconden timestamp tolerance.

## Conclusie

Het discrepancy-mechanisme is defensief en sluit nu aan op dezelfde provideridentity-, lifecycle- en recoverysemantiek als de rest van de scheduler. Er resteert geen zelfstandig SCHED-011-codegat. Alleen het exacte live 2Park post-End contract blijft een extern observatiepunt voor latere aanscherping van tests/defaults.