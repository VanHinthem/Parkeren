# Container image pipeline

## Doel

Bouw en publiceer reproduceerbare Docker-images vanuit GitHub Actions, zodat de NAS images kan ophalen zonder lokaal vanuit Git te bouwen. Dit dossier is de leidende technische checklist voor dit werk. Werk de status en eerstvolgende stap bij voordat een werksessie of agent stopt.

## Vastgelegde besluiten

- De GitHub-repository blijft private; de GHCR-container packages worden public zodat de NAS images kan pullen.
- `develop`: relevante backend- en frontendwijzigingen doorlopen de bijbehorende CI-checks; bij ten minste één relevante wijziging worden na geslaagde toepasselijke checks de app-image en TwoPark-mock-image gebouwd. Beide krijgen dezelfde commit-SHA als immutable tag en een beweegbare `dev`-tag. Wijzigingen zonder relevante buildinput, zoals alleen documentatie, slaan checks en imagebuilds over.
- De dev-stack gebruikt de mock-provider, niet de echte TwoPark-service.
- `main`: relevante wijzigingen doorlopen de bijbehorende CI-checks, maar bouwen/publiceren geen image.
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

- Laat de bestaande CI-path filters bepalen welke backend- en frontendchecks nodig zijn op pull requests en branch-pushes; houd handmatige runs en release-tagvalidatie expliciet.
- Op een push naar `develop` of PR naar `develop` met relevante backend- of frontendbuildinput: laat de toepasselijke checks slagen, bouw app en mock, en geef beide images dezelfde commit-SHA-tag plus `dev`. Sla checks en images over als geen van beide path filters matcht.
- Op gewone pushes naar `main`: voer alleen de checks uit waarvan de path filters matchen; bouw geen images.
- Op een `vMAJOR.MINOR.PATCH` releasetag: valideer dat de getagde commit op `main` staat en de CI-tests voor die commit geslaagd zijn; bouw uitsluitend de productie-app-image met SemVer- en SHA-tag.
- In fase 3 blijven alle gebouwde images lokaal op de tijdelijke CI-runner en wordt niets naar GHCR gepusht. Fase 4 voegt scans en pas daarna publicatie toe.
- Acceptatie: relevante dev-PR/push-wijzigingen bouwen app en mock na geslaagde toepasselijke checks met dezelfde commit-SHA en `dev`-alias; niet-relevante wijzigingen slaan checks en images over; releasetags op geteste `main`-commits bouwen alleen de productie-app; gewone `main`-commits bouwen geen images; er is geen GHCR-login of push.

### 4. Security gates en GHCR

- Configureer GHCR-pakketten als public en bevestig dat de repository private blijft.
- De eerste GHCR-push maakt de packages aan; zet `parkeren-app` en `parkeren-two-park-mock` daarna eenmalig via de GitHub-package-instellingen op public en verifieer een anonieme pull. Voeg geen PAT toe aan Actions alleen om package visibility te beheren.
- Scan repositorywijzigingen op gelekte secrets en scan elke gebouwde image vóór publicatie op secrets en relevante bekende kwetsbaarheden.
- Voeg GHCR-login en image-push pas toe nadat de bron- en image-scans succesvol zijn.
- Gebruik secrets uitsluitend als runtimeconfiguratie op de NAS; geef ze niet mee als Docker build arguments of bestanden in de image.
- Controleer workflows en applicatielogs op het afdrukken van PINs, wachtwoorden, tokens, sleutels of connection strings. Voeg gerichte regressiechecks toe waar dit betrouwbaar te automatiseren is.
- Acceptatie: secret- of image-scanfouten blokkeren publicatie; normale workflowlogs bevatten geen secretwaarden; een anonieme pull van een image werkt zonder toegang tot de private bronrepository.

#### Implementatiekeuzes

- Gitleaks Action v3 scant de volledige gitgeschiedenis; PR-commentaar en scan-artifactuploads staan uit.
- Trivy Action v0.36.0 scant gebouwde images op secrets en HIGH/CRITICAL-kwetsbaarheden. Iedere scan heeft een niet-nul exitcode bij een bevinding; unfixed kwetsbaarheden worden niet genegeerd.
- De buildjob scant lokaal en uploadt daarna alleen het exacte gescande image-archief als artifact met retentie van één dag. Een aparte publisher-job met uitsluitend `packages: write` downloadt dat artifact en publiceert alleen op relevante develop-pushes of gevalideerde releasetags. PR-jobs loggen niet in bij GHCR en pushen nooit.
- PR-validatie in run 37305258414 slaagde voor Gitleaks, backend/frontendchecks, beide imagebuilds en alle vier Trivy-scans; alle publish-jobs werden overgeslagen. Featurebranch-push-run 37305245070 slaagde voor Gitleaks en tests en bouwde/pushte geen images.
- Na merge van PR #116 slaagde develop-push-run 37306419871 voor Gitleaks, backend/frontendchecks, beide imagebuilds, alle vier Trivy-scans en de development-publisher. De packages `parkeren-app` en `parkeren-two-park-mock` zijn public gezet; anonieme pulls van beide `dev`-tags slaagden vanuit een lege Docker-configuratie. Er zijn geen containers gestart en geen NAS-acties uitgevoerd.

### 5. NAS pull-instructie en overdracht

- Lever aparte voorbeeld-Composebestanden voor productie en development die gepubliceerde GHCR-images pullen; wijzig de actieve Compose-files en de 2Park-testupdate niet.
- Lever bij elk voorbeeld een `.env`-template. Houd echte secrets en omgevingsconfiguratie uitsluitend op de NAS.
- Documenteer handmatig pullen/starten en rollback via een eerdere immutable SHA- of releasetag; behoud bij productie de bestaande Compose-projectnaam en databasevolume-identiteit.
- Acceptatie: de NAS kan de gekozen publieke GHCR-image ophalen en de stack starten zonder Git checkout of lokale app-imagebuild. Voer dit pas uit wanneer de NAS-eigenaar de juiste configuratie heeft gezet.

## Validatiegates

- Bestaande relevante .NET- en frontendtests blijven slagen.
- Migratieketen slaagt op een lege PostgreSQL-database; blijvende productiegegevens worden niet gereset.
- Dev- en productie-PWA-builds slagen elk voor hun manifest-, icon- en PWA-checks.
- Workflowconfiguratie slaagt voor relevante en niet-relevante path-filterpaden op PR/push, voor release-tags en handmatige runs; `main`-push publiceert geen image.
- GHCR-publicatie vindt pas plaats na tests en beveiligingsscans.
- Geen NAS-deploy of databasebackuptaak toevoegen binnen dit werk.

## Voortgangscheckpoint

- Status: fasen 1-4 zijn afgerond en gemerged. De repo-deliverables van fase 5 zijn via PR #119 gemerged: zelfstandige productie- en development-Composevoorbeelden met env-templates en handmatige pull-/rollbackinstructies. De development-stack is door de NAS-eigenaar gestart en smoke-getest op een UGREEN DXP2800: database healthy, `/health` succesvol, een mock-parkeeractie gestart en gestopt, en een rebuild via de NAS-Dockerapp is door de gebruiker als succesvol bevestigd. Productie pull/start-acceptatie blijft afhankelijk van een gepubliceerde productie-releaseimage en bevestiging van de productieproject-/volume-identiteit. Issue #118 blijft open tot die acceptatie is afgehandeld. Release-tagvalidatie blijft bewust uitgesteld tot alle geplande wijzigingen op `main` staan.
- GitHub-tracking: initiatief #99; migratiefeature #102 via PR #103; PWA-feature #105 via PR #106; imageworkflowfeature #108 via PR #109; path-filteroptimalisatie #111 via PR #112; securitygates en GHCR-publicatie #115 via PR #116; Compose-imagevoorbeelden en overdracht #118 via PR #119.
- Laatst afgerond: docs-only-wijzigingen slaan relevante checks en imagejobs over; relevante develop-wijzigingen draaien checks, bouwen en scannen beide images en publiceren ze daarna; handmatige runs draaien checks zonder images te publiceren. De anonieme pulls van beide `dev`-images zijn lokaal bevestigd. De productie- en dev-voorbeeldcompose valideren met hun env-templates zonder imagebuilds; PR #119 slaagde voor de relevante workflowchecks.
- Volgende stap: herstel de releasejobvoorwaarden zodat de imagebuild na geslaagde backend-, frontend-, tag- en secret-gates doorgaat, ook wanneer de path-filterjob voor een tag is overgeslagen. Laat de fix via een PR naar `develop` reviewen en promoten; voer daarna de releasevalidatie opnieuw uit met een door de gebruiker bevestigde tag. `v0.1.0` bestaat op de huidige `main`-commit maar heeft geen productie-image gepubliceerd. Productie pull/start blijft wachten op een gepubliceerde productie-releaseimage en bevestiging van de productieprojectnaam en databasevolume-identiteit door de NAS-eigenaar.
- Blokkades/besluiten: er bestaat nog geen productie-releaseimage; de production-template bevat daarom uitsluitend een expliciete vervang-placeholder en mag die niet pullen voordat een release is gepubliceerd. De development-NAS-smoketest is geslaagd op een eigen dev-stack; er is geen productie-NAS-deploy, backupwijziging of database-reset uitgevoerd. Issue #118 blijft open en vraagt na afronding van productieacceptatie om handmatige sluiting.
- Laatste validatie: develop-push-run 37306419871 slaagde voor Gitleaks, backend/frontend, beide development-imagebuilds, vier Trivy-scans en de GHCR-publisher; releasejobs werden overgeslagen. Anonieme pulls slaagden voor `ghcr.io/vanhinthem/parkeren-app:dev` (digest `sha256:01c48e8b2232d1a341039b610a9689d91953201d2d7b923152e0b77b6e4200c9`) en `ghcr.io/vanhinthem/parkeren-two-park-mock:dev` (digest `sha256:0043eeb1f91af35137f7de3648f762dc1ff0dd30b784e8060838c08498546c54`). Beide Compose-voorbeelden slaagden voor `docker compose config --quiet` met de bijbehorende env-template. PR #119-checks slaagden voor pathfilters en Gitleaks; backend/frontend/build/publishjobs werden terecht overgeslagen. Op de UGREEN DXP2800 startten app (`:5082`), PostgreSQL (`healthy`) en mock (`:5083`); de gebruiker bevestigde `/health`, de mock start/stop-flow en een succesvolle rebuild via de NAS-Dockerapp. Run 37331912241 op tag `v0.1.0` slaagde voor tagvalidatie, Gitleaks en backend/frontendtests; run 37331913193 slaagde voor dezelfde gates. In beide runs werden `build-release-image` en `publish-release-image` overgeslagen, doordat de releasejobs impliciet afhankelijk bleven van de overgeslagen path-filterjob. Er is dus geen `v0.1.0`-image gepubliceerd; de workflowfix is in uitvoering op een featurebranch.

## Agent-werkwijze

1. Lees dit dossier, `docs/work/README.md`, de relevante code/tests en de actuele git-status voordat je iets wijzigt.
2. Zorg vóór implementatie dat iedere feature een GitHub-issue heeft met scope en acceptatiecriteria. Gebruik voor deze grotere pipeline een tracking issue met afzonderlijke issues voor zelfstandig reviewbare features/fases.
3. Maak een featurebranch vanaf de nieuwste `develop` en neem het issue-nummer op in de branchnaam; commit nooit rechtstreeks op `develop`.
4. Controleer of de checkpointstatus nog klopt. Wijzig geen latere fase zolang de huidige fase niet voldoet aan de acceptatiecriteria.
5. Voer alleen de eerstvolgende concrete stap uit. Houd wijzigingen beperkt tot die fase.
6. Na de eerste wijziging voer je direct de dichtstbijzijnde relevante test, build of securitycheck uit. Repareer lokale fouten en herhaal dezelfde check.
7. Werk na elke afgeronde fase de GitHub-issue en de voortgang, uitgevoerde commando's en relevante resultaten in dit dossier bij. Leg mislukte of niet-uitgevoerde validaties expliciet vast.
8. Maak aan het einde een PR naar `develop`, link het issue en laat de PR open voor review en handmatige afronding door de gebruiker. Omdat `develop` niet de standaardbranch is, sluiten GitHub-closing keywords het issue niet automatisch; meld na de merge als het issue nog handmatig gesloten moet worden. Merge de PR niet, squash niet en schakel geen auto-merge in.
9. Stop bij een benodigde gebruikerskeuze, ontbrekende NAS-toegang/configuratie of een migratierisico; noteer exact wat nodig is en ga niet stilzwijgend buiten scope.
10. Verwijder geen bestaande gebruikerswijzigingen, migratiebestanden of productiegegevens.
