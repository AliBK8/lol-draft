# Gateway (DESIGN.md §8.8): React build'i + YARP. Üç aşama: istemciyi Node'da derle, gateway'i SDK'da derle,
# ikisini aspnet imajında birleştir (web/dist → /app/wwwroot, WebClient.cs oradan sunar). Root olmayan kullanıcı.
# Bağlam repo kökü: docker build -f src/LolDraft.Gateway/Dockerfile .

FROM node:24-bookworm-slim AS web
WORKDIR /web
# Önce sadece paket listesi: kod değişip paketler değişmediyse npm ci katmanı önbellekten gelir.
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LolDraft.Gateway/LolDraft.Gateway.csproj src/LolDraft.Gateway/
RUN dotnet restore src/LolDraft.Gateway/LolDraft.Gateway.csproj
COPY src/LolDraft.Gateway/ src/LolDraft.Gateway/
RUN dotnet publish src/LolDraft.Gateway/LolDraft.Gateway.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "LolDraft.Gateway.dll"]
