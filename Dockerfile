FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["RadarChollos.csproj", "./"]
RUN dotnet restore "./RadarChollos.csproj"

COPY . .
RUN dotnet publish "RadarChollos.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa final de ejecución (con soporte ASP.NET Core)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RadarChollos.dll"]