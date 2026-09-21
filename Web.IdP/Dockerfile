# syntax=docker/dockerfile:1

FROM node:24.21.0-bookworm-slim AS frontend-build

WORKDIR /src/Web.IdP/ClientApp
COPY Web.IdP/ClientApp/package.json Web.IdP/ClientApp/package-lock.json ./
RUN --mount=type=cache,id=hybrididp-npm,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY Web.IdP/ClientApp/ ./
COPY Web.IdP/Pages/ /src/Web.IdP/Pages/
COPY Web.IdP/Views/ /src/Web.IdP/Views/
RUN npm run build
RUN test -f /src/Web.IdP/wwwroot/dist/.vite/manifest.json

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-publish

WORKDIR /src
COPY Core.Domain/Core.Domain.csproj Core.Domain/
COPY Core.Application/Core.Application.csproj Core.Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Infrastructure.Migrations.SqlServer/Infrastructure.Migrations.SqlServer.csproj Infrastructure.Migrations.SqlServer/
COPY Infrastructure.Migrations.Postgres/Infrastructure.Migrations.Postgres.csproj Infrastructure.Migrations.Postgres/
COPY Web.IdP/Web.IdP.csproj Web.IdP/
RUN dotnet restore Web.IdP/Web.IdP.csproj --disable-parallel

COPY . .
COPY --from=frontend-build /src/Web.IdP/wwwroot/dist /src/Web.IdP/wwwroot/dist
RUN test -f /src/Web.IdP/wwwroot/dist/.vite/manifest.json

RUN dotnet publish Web.IdP/Web.IdP.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
COPY --from=backend-publish /app/publish ./
COPY --from=frontend-build /src/Web.IdP/wwwroot/dist /app/wwwroot/dist
RUN test -f /app/wwwroot/dist/.vite/manifest.json

ENTRYPOINT ["dotnet", "Web.IdP.dll"]
