# Ontwikkelomgeving

## Solution

De backend gebruikt .NET 10 en is opgenomen in `Parkeren.slnx`. De solution volgt ADR #88. De PWA staat in `src/Parkeren.Web` en gebruikt React, TypeScript en Vite.

## Lokaal starten

Backend:

```bash
dotnet restore Parkeren.slnx
dotnet build Parkeren.slnx
dotnet test Parkeren.slnx
dotnet run --project src/Parkeren.Api
```

Frontend:

```bash
cd src/Parkeren.Web
npm install
npm run dev
```

Tijdens lokale frontend-development proxyt Vite `/api` naar de lokale ASP.NET Core API.

## PostgreSQL en EF Core

`Parkeren.Infrastructure` bevat `ParkerenDbContext`. De databaseprovider is Npgsql/PostgreSQL. Migrations staan onder `Persistence/Migrations`.

De initiële migration bevat bewust nog geen tabellen; de eerste domeintabellen worden toegevoegd wanneer de betreffende verticale slice wordt geïmplementeerd. Zo loopt het schema mee met daadwerkelijk domeingedrag.

De health endpoint controleert ook of de DbContext/database bereikbaar is.

## Dev/test Compose-stack

Maak lokaal een `.env` op basis van `.env.example` en gebruik een eigen sterk development-wachtwoord. Omdat de primaire Compose-file onder `deploy/` staat, geven we het repository-root `.env` expliciet mee met `--env-file .env`. De developmentstack activeert expliciet het `mock`-profile.

```bash
docker compose --env-file .env --profile mock -p parkeren-dev \
  -f deploy/compose.yml \
  -f deploy/compose.dev.yml \
  up -d --build
```

Deze stack gebruikt:
- app: localhost:5080;
- 2Park mock: localhost:5081;
- een eigen PostgreSQL-volume onder Compose project `parkeren-dev`;
- `ParkingProvider:Type=TwoParkMock`.

Gebruik voor productie een andere Compose projectnaam/configuratie en deel nooit het dev-volume.

## Productiebuild

De app-container heeft een multi-stage build. Node bouwt `Parkeren.Web`; `dist` wordt als `wwwroot` in ASP.NET Core opgenomen. API en PWA draaien daarmee uit dezelfde container.

## Productiedeployment en scheduler

V1 ondersteunt **exact één actieve `Parkeren.Api`-instance**. Die instance bevat ook `VisitSchedulerWorker`.

Daarom gelden voor productie de volgende correctness-regels:

- draai exact één `app`-container;
- gebruik geen `docker compose --scale app=N` met `N > 1`;
- gebruik geen rolling deployment waarbij oude en nieuwe app-instances tegelijk actief zijn;
- een update/restart vervangt de bestaande app-instance in plaats van er tijdelijk een tweede naast te starten.

De scheduler gebruikt database-locking voor concurrency binnen de actieve applicatie, maar V1 heeft bewust geen distributed scheduler lease/heartbeat voor meerdere app-instances. Startup recovery mag daardoor achtergelaten `Claimed` scheduler-work behandelen als state van de vorige, niet meer actieve procesinstantie.

Zodra horizontale schaal, replicas of overlappende zero-downtime deployments gewenst worden, moet het scheduler deployment- en recoverycontract eerst opnieuw worden ontworpen voordat dat als ondersteund geldt.

## CI

GitHub Actions valideert .NET restore/build/test en frontend install/build/test.

## Secrets

Commit nooit echte databasewachtwoorden, PINs, sessiesecrets of 2Park-credentials. `.env.example` bevat uitsluitend voorbeeldnamen/waarden.
