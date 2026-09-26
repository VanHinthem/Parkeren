# Ontwikkelomgeving

## Solution

De backend gebruikt .NET 10 en is opgenomen in `Parkeren.slnx`. De solution volgt ADR #88:

- Parkeren.Domain
- Parkeren.Application
- Parkeren.Infrastructure
- Parkeren.Api
- Parkeren.TwoParkMock
- Domain/Application/Integration testprojecten

De PWA staat in `src/Parkeren.Web` en gebruikt React, TypeScript en Vite.

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

Tijdens lokale frontend-development proxyt Vite verzoeken onder `/api` naar de lokale ASP.NET Core API.

## Productiebuild

De app-container heeft een multi-stage build. Node bouwt `Parkeren.Web`; de resulterende `dist` wordt als `wwwroot` in de ASP.NET Core runtime-image opgenomen. ASP.NET Core serveert daardoor API en PWA vanuit dezelfde container.

Client-side routes vallen terug op `index.html`; `/api/*` en `/health` blijven server-endpoints.

## CI

GitHub Actions valideert op pushes en pull requests:
- .NET restore/build/test;
- frontend install/build/test.

De CI gebruikt dezelfde .NET- en Node-majorversies als de gekozen V1-stack.

## Docker

`deploy/compose.yml` bevat de productieachtige app/database-stack en een optionele 2Park mock profile. Productie en dev/test gebruiken afzonderlijke Compose-projecten, databases, volumes, netwerken en secrets volgens ADR #87.

## Secrets

Commit nooit echte databasewachtwoorden, PINs, sessiesecrets of 2Park-credentials. `.env.example` bevat uitsluitend voorbeeldnamen/waarden.
