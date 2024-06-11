#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 80:8080
#EXPOSE 443
EXPOSE 8081
## Setup NodeJS
#USER root
#RUN apt-get update
#RUN apt-get -y install curl
#RUN curl -sL https://deb.nodesource.com/setup_20.x | bash
#RUN apt-get -y install nodejs

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
## Setup NodeJS
##USER root
#RUN apt-get update
#RUN apt-get install curl
#RUN curl -sL https://deb.nodesource.com/setup_20.x | bash
#RUN apt-get -y install nodejs
#RUN npm install -g @angular/cli

# Build application
RUN dotnet workload update

USER app
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Beid/DemoWebApp/DemoWebApp.csproj", "DemoWebApp/"]
#RUN dotnet restore "./JdcTopo.Web/JdcTopo.Web.csproj"
COPY ["Beid/DemoWebApp", "DemoWebApp/"]

USER root
WORKDIR /src
RUN chown app -R /src
RUN mkdir /app && mkdir /app/build
RUN chown app -R /app

#USER app
#WORKDIR "/src/DemoWebApp/ClientApp"
#RUN npm install
#RUN npm run build -- --configuration=production
#RUN npm run build:ssr

WORKDIR "/src"
RUN dotnet restore "./DemoWebApp/DemoWebApp.csproj"
WORKDIR "/src/DemoWebApp"
RUN dotnet build "./DemoWebApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DemoWebApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DemoWebApp.dll"]