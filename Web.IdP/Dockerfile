FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first for better restore caching
COPY HybridAuthIdP.sln ./
COPY Core.Domain/Core.Domain.csproj Core.Domain/
COPY Core.Application/Core.Application.csproj Core.Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Infrastructure.Migrations.SqlServer/Infrastructure.Migrations.SqlServer.csproj Infrastructure.Migrations.SqlServer/
COPY Infrastructure.Migrations.Postgres/Infrastructure.Migrations.Postgres.csproj Infrastructure.Migrations.Postgres/
COPY Web.IdP/Web.IdP.csproj Web.IdP/

RUN dotnet restore Web.IdP/Web.IdP.csproj

# Copy source (frontend assets should already exist under wwwroot/dist)
COPY . .

RUN test -f ./Web.IdP/wwwroot/dist/.vite/manifest.json || (echo "Missing frontend build output. Run deployment/deploy-idp.sh first." && exit 1)

RUN dotnet publish Web.IdP/Web.IdP.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Web.IdP.dll"]
