# NAS deployment — ACC en DEV

PRD (`parkeren.vanhinthem.nl`) is buiten scope. Gebruik deze templates **alleen voor nieuwe ACC en DEV stacks**. Beide gebruiken uitsluitend TwoParkMock; geen echte 2Park-credentials.

## Mappen op de NAS

Maak twee afzonderlijke mappen aan (locaties zijn voorbeelden):

```
docker/parkeren-acc/
  docker-compose.yml
  .env
docker/parkeren-dev/
  docker-compose.yml
  .env
  source/               # wordt automatisch geïnitialiseerd
```

Kopieer voor elke map de inhoud uit `deploy/nas/acc/` of `deploy/nas/dev/`. Hernoem `.env.example` naar `.env`. Kies **een uniek, sterk databasewachtwoord per omgeving** en vul voor de eerste start `PARKEREN_BOOTSTRAP_ADMIN_USERNAME` en een zescijferige `PARKEREN_BOOTSTRAP_ADMIN_PIN` in. Bewaar echte secrets uitsluitend op de NAS, nooit in GitHub.

Geen Git, Node of .NET-installatie op de NAS-host nodig: alles draait in containers. Docker Compose moet wel de projectbestanden vanuit de gekozen map kunnen benaderen.

## ACC (acceptatie)

- URL: `https://parkeren-acc.vanhinthem.nl`
- NPM forwards naar NAS-hostpoort `5082` (of `PARKEREN_APP_PORT`).
- De app- en mock-images komen uit GHCR met dezelfde `PARKEREN_ACC_IMAGE_TAG`; standaard `acc`.
- De tag `acc` wordt gepubliceerd na een geslaagde push naar `develop`, **niet** bij een feature-branch commit.
- Voor reproduceerbare builds gebruik `acc-<volledige-commit-sha>` in plaats van `acc`.
- `COMPOSE_PROJECT_NAME=parkeren-acc`, met eigen database- en mockvolumes.

Binnen de ACC-map:

```sh
docker compose config --quiet
docker compose pull
docker compose up -d
```

## DEV (feature branch, watch/HMR)

- URL: `https://parkeren-dev.vanhinthem.nl`
- NPM forwards naar NAS-hostpoort `5084` (of `PARKEREN_WEB_PORT`) met **WebSocket support** aan.
- De branch-controller (Alpine met ingebouwde Git) haalt met `PARKEREN_ACTIVE_BRANCH` de geselecteerde GitHub-branch op.
- Iedere `PARKEREN_GIT_POLL_SECONDS` seconden (standaard 15) vergelijkt de controller de commit-SHA en werkt `source/` bij.
- De frontend draait Vite en de API draait `dotnet watch`; wijzigingen worden zonder app-image-build opgepakt.
- DEV heeft eigen database, mockdata en npm/NuGet caches; geen directe koppeling met ACC of PRD.
- De gebruikte TwoParkMock-container is voorlopig de `acc` image en wordt niet automatisch opnieuw gepulld bij branch-commits.

Binnen de DEV-map:

```sh
docker compose config --quiet
docker compose up -d
docker compose logs -f branch-controller web api
```

Eerste start vereist een bereikbare publieke GitHub-repository en tijd voor het ophalen van broncode plus npm/.NET dependencies. **De DEV-app is nog niet klaar zodra alle containers alleen 'running' tonen**: controleer of de API zonder fouten start en of Vite bereikbaar is.

Om de actieve branch te veranderen: pas `PARKEREN_ACTIVE_BRANCH` aan in `.env` en maak **alleen de branch-controller opnieuw** met `docker compose up -d --force-recreate branch-controller`. De controller verandert de checkout; Vite en `dotnet watch` reageren op bestandswijzigingen. Bij wijzigingen in `package-lock.json`, `.csproj`, runtimeconfiguratie of migraties kan een expliciete herstart nodig zijn:

```sh
docker compose up -d --force-recreate web api
```

De browser-PWA/serviceworker staat tijdens normale Vite-ontwikkeling uit. Voor serviceworker-/installatie-/push-tests gebruiken we de apart gebouwde ACC-app, of later een afzonderlijke opt-in PWA-dev modus.

## Veiligheid en bekende beperkingen

- Stel poorten `5082` en `5084` bij voorkeur alleen op het vertrouwde LAN beschikbaar; zet authenticatie/toegangsrestricties vóór dev-endpoints, vooral `/api/dev/*`. De huidige ACC/API draait met `ASPNETCORE_ENVIRONMENT=Development`, waarin diagnostische endpoints beschikbaar zijn.
- **Geen Docker socket** in de branch-controller; deze beheert alleen een expliciete `source/` checkout en kan geen containers verwijderen.
- De source-map moet bij de eerste DEV-start leeg zijn. De controller gebruikt `git reset --hard` en `git clean`: maak daar geen handmatige wijzigingen of gebruikersbestanden.
- Niet twee DEV-controllers naar dezelfde source-map laten schrijven.
- Eén API-instance per omgeving: de applicatie heeft geen distributed scheduler lease.
- Een feature-branch wordt automatisch gesynchroniseerd **ongeacht CI-status**. Dit is bewust voor snelle DEV-feedback, niet geschikt als acceptatie-eis.
- Bij een ongeldige/verdwenen branch houdt de controller de laatst opgehaalde checkout aan en probeert hij opnieuw.
- De huidige config is een eerste versie; na de eerste NAS-start testen we Git-synchronisatie, frontend HMR, .NET reload, branchswitch en NPM WebSockets voordat we hem als gereed beschouwen.
- Verwijder nooit volumes van PRD. Gebruik voor de nieuwe ACC/DEV stacks ook niet `docker compose down -v`, tenzij je hun testdata bewust wilt wissen.

## Git workflow

`feature/*` → DEV live checkout (indien geselecteerd) → PR naar `develop` → groene CI → ACC image `:acc` → release naar PRD via de bestaande releaseprocedure. PRD-configuratie blijft intact.
