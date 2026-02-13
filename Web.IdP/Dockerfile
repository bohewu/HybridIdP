FROM node:22-bookworm-slim AS frontend-build

WORKDIR /src/Web.IdP/ClientApp
COPY Web.IdP/ClientApp/package.json Web.IdP/ClientApp/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY Web.IdP/ClientApp/ ./
RUN npm run build
RUN test -f /src/Web.IdP/wwwroot/dist/.vite/manifest.json

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-publish

WORKDIR /src
COPY . .
COPY --from=frontend-build /src/Web.IdP/wwwroot/dist /src/Web.IdP/wwwroot/dist
RUN test -f /src/Web.IdP/wwwroot/dist/.vite/manifest.json

RUN dotnet restore Web.IdP/Web.IdP.csproj
RUN dotnet publish Web.IdP/Web.IdP.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
COPY --from=backend-publish /app/publish ./
COPY --from=frontend-build /src/Web.IdP/wwwroot/dist /app/wwwroot/dist
RUN test -f /app/wwwroot/dist/.vite/manifest.json

ENTRYPOINT ["dotnet", "Web.IdP.dll"]
