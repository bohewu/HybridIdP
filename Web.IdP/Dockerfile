# syntax=docker/dockerfile:1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

FROM node:26.11.1-bookworm-slim@sha256:86f07bc9c5dce4578cf37e5a418b7bfc7f817cda25cde66e2b66e95ed86c4567 AS frontend-build

WORKDIR /src/Web.IdP/ClientApp
COPY Web.IdP/ClientApp/package.json Web.IdP/ClientApp/package-lock.json ./
RUN --mount=type=cache,id=hybrididp-npm,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY Web.IdP/ClientApp/ ./
COPY Web.IdP/Pages/ /src/Web.IdP/Pages/
COPY Web.IdP/Views/ /src/Web.IdP/Views/
RUN npm run build
RUN test -f /src/Web.IdP/wwwroot/dist/.vite/manifest.json

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS backend-publish

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final

WORKDIR /app
COPY --from=backend-publish /app/publish ./
COPY --from=frontend-build /src/Web.IdP/wwwroot/dist /app/wwwroot/dist
RUN test -f /app/wwwroot/dist/.vite/manifest.json

ENTRYPOINT ["dotnet", "Web.IdP.dll"]
