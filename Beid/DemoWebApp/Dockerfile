FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Beid/DemoWebApp/DemoWebApp.csproj", "DemoWebApp/"]
RUN dotnet restore "./DemoWebApp/DemoWebApp.csproj"
COPY ["Beid/DemoWebApp", "DemoWebApp/"]
WORKDIR "/src/DemoWebApp"
RUN dotnet build "./DemoWebApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DemoWebApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DemoWebApp.dll"]
