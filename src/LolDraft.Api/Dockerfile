# Oyun API'si (DESIGN.md §8.8): derleme SDK imajında, çalışma küçük aspnet imajında, root olmayan kullanıcıyla.
# Bağlam repo kökü: docker build -f src/LolDraft.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
# Önce sadece proje dosyaları: kod değişip paketler değişmediyse restore katmanı önbellekten gelir.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LolDraft.Api/LolDraft.Api.csproj src/LolDraft.Api/
COPY src/LolDraft.Application/LolDraft.Application.csproj src/LolDraft.Application/
COPY src/LolDraft.Domain/LolDraft.Domain.csproj src/LolDraft.Domain/
COPY src/LolDraft.Engine/LolDraft.Engine.csproj src/LolDraft.Engine/
COPY src/LolDraft.Infrastructure/LolDraft.Infrastructure.csproj src/LolDraft.Infrastructure/
RUN dotnet restore src/LolDraft.Api/LolDraft.Api.csproj
# Şampiyon dosyası derlemeye gömülür (Infrastructure, EmbeddedResource).
COPY data/champions.json data/
COPY src/LolDraft.Api/ src/LolDraft.Api/
COPY src/LolDraft.Application/ src/LolDraft.Application/
COPY src/LolDraft.Domain/ src/LolDraft.Domain/
COPY src/LolDraft.Engine/ src/LolDraft.Engine/
COPY src/LolDraft.Infrastructure/ src/LolDraft.Infrastructure/
RUN dotnet publish src/LolDraft.Api/LolDraft.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Maç veritabanının klasörü. Boş bir named volume ilk bağlandığında bu klasörün sahibini devralır: app kullanıcısı
# (APP_UID, 1654) yazabilsin diye root değil onun.
RUN mkdir /data && chown "$APP_UID" /data
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "LolDraft.Api.dll"]
