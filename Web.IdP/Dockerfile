FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Use Taiwan apt mirror for faster downloads (ftp.tw.debian.org)
RUN sed -i 's|deb.debian.org|ftp.tw.debian.org|g' /etc/apt/sources.list.d/debian.sources 2>/dev/null || \
    sed -i 's|deb.debian.org|ftp.tw.debian.org|g' /etc/apt/sources.list 2>/dev/null || true

# Install Node.js
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && \
    apt-get install -y nodejs

WORKDIR /source
COPY . .
WORKDIR /source/Web.IdP/ClientApp
RUN npm install
RUN npm run build

WORKDIR /source
RUN dotnet restore Web.IdP/Web.IdP.csproj
RUN dotnet publish Web.IdP/Web.IdP.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
# Explicitly copy the frontend build artifacts in case dotnet publish missed them
COPY --from=build /source/Web.IdP/wwwroot/dist ./wwwroot/dist
ENTRYPOINT ["dotnet", "Web.IdP.dll"]