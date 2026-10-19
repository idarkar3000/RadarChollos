FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY RadarChollos/RadarChollos.csproj RadarChollos/
RUN dotnet restore RadarChollos/RadarChollos.csproj

COPY RadarChollos/ RadarChollos/
RUN dotnet publish RadarChollos/RadarChollos.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RadarChollos.dll"]
