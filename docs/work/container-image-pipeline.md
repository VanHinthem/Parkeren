# Container image pipeline

## Doel

Bouw en publiceer reproduceerbare Docker-images vanuit GitHub Actions, zodat de NAS images kan ophalen zonder lokaal vanuit Git te bouwen. Dit dossier is de leidende technische checklist voor dit werk. Werk de status en eerstvolgende stap bij voordat een werksessie of agent stopt.

## Vastgelegde besluiten

- De GitHub-repository blijft private; de GHCR-container packages worden public zodat de NAS images kan pullen.
- `develop`: iedere commit doorloopt de CI-tests en bouwt de app-image en TwoPark-mock-image. Beide krijgen dezelfde commit-SHA als immutable tag en een beweegbare `dev`-tag.
- De dev-stack gebruikt de mock-provider, niet de echte TwoPark-service.
- `main`: commits doorlopen automatisch tests, maar bouwen/publiceren geen image.
- Een release-Git-tag zoals `v1.2.3` triggert de productie-app-imagebuild. Alleen de app-image wordt voor productie gepubliceerd; de mock wordt niet gepubliceerd als productie-release.
- De release-image krijgt de SemVer-tag en de commit-SHA. De tag moet wijzen naar een geteste commit op `main`.
- Productiebuilds zijn alleen voor de app; de NAS haalt de gewenste image handmatig op. NAS-deployautomatisering valt buiten scope.
- Runtime secrets blijven op de NAS en worden niet in broncode, build arguments, image layers of build output opgenomen.
- Beide PWA's hebben hun eigen domein: `parkeren-dev.vanhinthem.nl` en `parkeren.vanhinthem.nl`. De dev-build krijgt een herkenbare naam en `DEV`-variant van het icoon; de productienaam en het productie-icoon blijven ongewijzigd. De dev-themakleur hoeft niet af te wijken.
- Databasebackups en hun retentie/uitvoering vallen buiten scope van deze pipeline.

## Scopegrenzen

In scope: CI/CD-workflows voor tests en imagebouw, GHCR-publicatie, image tags, secret/image-validatie, EF Core-migraties in dev en productie, PWA buildvariant en documentatie voor handmatig pullen op de NAS.

Buiten scope: automatisch deployen naar de NAS, het ontwerpen of uitvoeren van de NAS-backupstrategie, en wijzigingen aan functionele parkeerregels of providerintegraties.

## Uitvoeringsfasen

### 1. Dev- en productie-schema via EF-migraties

- Laat alle omgevingen `Database.MigrateAsync()` gebruiken; verwijder de Development-only `EnsureCreatedAsync()`-route.
- Behoud de bestaande EF-migratiebestanden. Reset alleen de dev-database eenmalig; productiegegevens en migratiehistorie worden niet verwijderd.
- Controleer de migratieketen op een lege PostgreSQL-database en voer de relevante .NET-tests uit.
- Acceptatie: een lege dev-database wordt bij API-start met de volledige migratieketen aangemaakt en bestaande productie blijft de normale migraties uitvoeren.

### 2. PWA-identiteit per buildvariant

- Maak manifestnaam en iconen buildvariant-afhankelijk: Parkeren voor productie, Parkeren Dev plus een duidelijk `DEV`-kenmerk voor dev.
- Lever voor dev zowel de 192px- als 512px-iconvariant; de 192px-afbeelding wordt ook binnen de app-shell gebruikt.
- Behoud de bestaande domeinen en scope `/`; verschillende origins zorgen voor gescheiden service workers en webopslag.
- Werk PWA-checks/tests bij voor beide varianten.
- Acceptatie: productie- en dev-manifesten en iconen zijn onderscheidbaar, beide builds slagen voor PWA-checks, en de bestaande app-shell toont het juiste icoon.

### 3. Workflow- en imagebouw

- Behoud tests op pull requests en pushes volgens de bestaande CI-path filters.
- Op iedere push naar `develop`: vereiste tests slagen, bouw app en mock, en geef beide images dezelfde commit-SHA-tag plus `dev`.
- Op gewone pushes naar `main`: voer tests uit, maar publiceer geen images.
- Op een `v*` releasetag: valideer dat de getagde commit op `main` staat en de CI-tests voor die commit geslaagd zijn; bouw en publiceer uitsluitend de productie-app-image met SemVer- en SHA-tag.
- Maak geen image-build of push voordat alle beveiligingscontroles voor die image slagen.
- Acceptatie: de tags verwijzen naar de bedoelde commit; app en mock in dev hebben dezelfde SHA; release tags bouwen alleen de app; normale `main`-commits publiceren niets.

### 4. Security gates en GHCR

- Configureer GHCR-pakketten als public en bevestig dat de repository private blijft.
- Scan repositorywijzigingen op gelekte secrets en scan elke gebouwde image vóór publicatie op secrets en relevante bekende kwetsbaarheden.
- Gebruik secrets uitsluitend als runtimeconfiguratie op de NAS; geef ze niet mee als Docker build arguments of bestanden in de image.
- Controleer workflows en applicatielogs op het afdrukken van PINs, wachtwoorden, tokens, sleutels of connection strings. Voeg gerichte regressiechecks toe waar dit betrouwbaar te automatiseren is.
- Acceptatie: secret- of image-scanfouten blokkeren publicatie; normale workflowlogs bevatten geen secretwaarden; een anonieme pull van een image werkt zonder toegang tot de private bronrepository.

### 5. NAS pull-instructie en overdracht

- Pas de Compose-deployconfiguratie aan om gepubliceerde images te gebruiken in plaats van lokaal te bouwen.
- Houd secrets en omgevingsconfiguratie op de NAS; documenteer welke image/tag daar handmatig wordt opgehaald en gestart.
- Documenteer rollback via een eerdere SHA- of releasetag.
- Acceptatie: de NAS kan de gekozen publieke GHCR-image ophalen en de stack starten zonder Git checkout of lokale app-imagebuild. Voer dit pas uit wanneer de NAS-eigenaar de juiste configuratie heeft gezet.

## Validatiegates

- Bestaande relevante .NET- en frontendtests blijven slagen.
- Migratieketen slaagt op een lege PostgreSQL-database; blijvende productiegegevens worden niet gereset.
- Dev- en productie-PWA-builds slagen elk voor hun manifest-, icon- en PWA-checks.
- Workflowconfiguratie slaagt voor beide triggerpaden: `develop`-push en `v*`-tag; `main`-push publiceert geen image.
- GHCR-publicatie vindt pas plaats na tests en beveiligingsscans.
- Geen NAS-deploy of databasebackuptaak toevoegen binnen dit werk.

## Voortgangscheckpoint

- Status: planning vastgelegd; implementatie is nog niet gestart.
- GitHub-tracking: initiatief #99; eerste documentatiefeature #100.
- Laatst afgerond: gebruikerskeuzes voor branchbeleid, publieke GHCR-images, releasetags, mock op dev, migraties in alle omgevingen en PWA-herkenbaarheid zijn verzameld.
- Volgende stap: fase 1, migratiegedrag in `Program.cs` aanpassen zodat dev ook EF Core-migraties gebruikt; daarna de migratieketen en relevante tests valideren.
- Blokkades/besluiten: geen inhoudelijke blokkades bekend. Voor fase 5 is NAS-configuratie nodig; deploy blijft handmatig en buiten scope.
- Laatste validatie: nog niet uitgevoerd; er zijn nog geen implementatiewijzigingen.

## Agent-werkwijze

1. Lees dit dossier, `docs/work/README.md`, de relevante code/tests en de actuele git-status voordat je iets wijzigt.
2. Zorg vóór implementatie dat iedere feature een GitHub-issue heeft met scope en acceptatiecriteria. Gebruik voor deze grotere pipeline een tracking issue met afzonderlijke issues voor zelfstandig reviewbare features/fases.
3. Maak een featurebranch vanaf de nieuwste `develop` en neem het issue-nummer op in de branchnaam; commit nooit rechtstreeks op `develop`.
4. Controleer of de checkpointstatus nog klopt. Wijzig geen latere fase zolang de huidige fase niet voldoet aan de acceptatiecriteria.
5. Voer alleen de eerstvolgende concrete stap uit. Houd wijzigingen beperkt tot die fase.
6. Na de eerste wijziging voer je direct de dichtstbijzijnde relevante test, build of securitycheck uit. Repareer lokale fouten en herhaal dezelfde check.
7. Werk na elke afgeronde fase de GitHub-issue en de voortgang, uitgevoerde commando's en relevante resultaten in dit dossier bij. Leg mislukte of niet-uitgevoerde validaties expliciet vast.
8. Maak aan het einde een PR naar `develop`, link het issue en laat de PR open voor review en handmatige afronding door de gebruiker. Merge de PR niet, squash niet en schakel geen auto-merge in.
9. Stop bij een benodigde gebruikerskeuze, ontbrekende NAS-toegang/configuratie of een migratierisico; noteer exact wat nodig is en ga niet stilzwijgend buiten scope.
10. Verwijder geen bestaande gebruikerswijzigingen, migratiebestanden of productiegegevens.
