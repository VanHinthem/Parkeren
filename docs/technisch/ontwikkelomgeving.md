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

Maak lokaal een `.env` op basis van `.env.example` en gebruik een eigen sterk development-wachtwoord.

```bash
docker compose -p parkeren-dev \
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

## CI

GitHub Actions valideert .NET restore/build/test en frontend install/build/test.

## Secrets

Commit nooit echte databasewachtwoorden, PINs, sessiesecrets of 2Park-credentials. `.env.example` bevat uitsluitend voorbeeldnamen/waarden.
