# SCHED-017 — provider timestampmatching is strenger dan bevestigd 2Park-gedrag

**Status:** ⚠️ Bevinding bevestigd  
**Prioriteit:** hoog  
**Raakt:** initiële Start, continuation, unknown/reconciliation en discrepancy-detectie.

## Bevestigd providerfeit

De live 2Park-tests in issue #71 hebben vastgelegd dat provider timestamps enkele seconden kunnen afwijken van de lokaal aangevraagde duur. Daarom is al besloten dat applicatiebeleid niet uit exacte read-back seconden mag worden afgeleid.

## As-built matching

### Directe start-readback

`StartVisitProviderExecutor` accepteert een mutation na read-back alleen wanneer onder andere geldt:

```text
remote.Start == PlannedStartAt
remote.End   == PlannedEndAt
status       == active
```

Dat is exacte `DateTimeOffset`-gelijkheid.

### Reconciliation

`StartVisitProviderReconciler` gebruikt `TimestampTolerance = 1 milliseconde` en vereist voor een definitieve match ook dat de eindtijd binnen die tolerantie valt. Wanneer een provider action-id bekend is, helpt die bij kandidaatselectie, maar de eindtijdcontrole blijft vrijwel exact.

### Periodieke discrepancy

Ook `ReconcileActiveProviderActionsAsync` behandelt een eindtijdverschil vanaf ongeveer 1 ms als mismatch.

## Bevinding

Deze matchingregels zijn aantoonbaar strenger dan het reeds waargenomen echte providercontract. Een geldige 2Park-action die enkele seconden genormaliseerd is kan daardoor:

- na succesvolle Start als `Unknown` worden opgeslagen;
- tijdens reconciliation niet bevestigd worden;
- onnodig `Reconciling` blijven;
- later als provider discrepancy worden gemarkeerd;
- scheduler continuation blokkeren.

Dit is extra kritisch omdat de recoveryarchitectuur terecht weigert blind opnieuw te muteren. Een te strikte matcher kan daardoor een veilige recovery bewust laten vastlopen.

## Ontwerprichtingen voor later

Nog geen fix in deze audit. Waarschijnlijk moet onderscheid worden gemaakt tussen:

- **identiteit** van een provideraction: bij voorkeur provider action-id + product + kenteken;
- **functionele geplande grenzen**: lokale snapshot/planning;
- **provider-observatie**: werkelijke timestamps met bekende provider-tolerantie.

Exacte timestamps zouden niet het primaire identiteitscriterium moeten zijn wanneer een provider action-id beschikbaar is.

## Onduidelijkheden / open vragen

1. Wat is de maximaal waargenomen/gedocumenteerde 2Park-afwijking in seconden?
2. Normaliseert 2Park alleen de eindtijd of ook de starttijd?
3. Kan een provider action-id ooit ontbreken terwijl de mutation toch uitgevoerd is?
4. Welke combinatie van product, kenteken, action-id en tijdwindow is voldoende om een match ondubbelzinnig te maken?
5. Wanneer is een tijdverschil groot genoeg om wél een echte discrepancy te openen?

## Verificatiecriteria

Na een oplossing moeten tests providerresponses met realistische secondenafwijking afdekken voor initial Start, scheduled continuation, timeout-reconciliation en periodic discrepancy. Een echte mismatch moet nog steeds detecteerbaar blijven.