FROM node:24-alpine AS web-build
ARG PARKEREN_BUILD_ID=local
ARG PARKEREN_APP_VERSION=local
ARG PARKEREN_APP_VARIANT=production
ENV PARKEREN_BUILD_ID=$PARKEREN_BUILD_ID PARKEREN_APP_VERSION=$PARKEREN_APP_VERSION PARKEREN_APP_VARIANT=$PARKEREN_APP_VARIANT
WORKDIR /web
COPY src/Parkeren.Web/package*.json ./
RUN npm install
COPY src/Parkeren.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY . .
RUN dotnet publish src/Parkeren.Api/Parkeren.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=web-build /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Parkeren.Api.dll"]
