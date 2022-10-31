#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /app
EXPOSE 80
EXPOSE 443

WORKDIR /src
COPY ["SyncStreamAPI/SyncStreamAPI.csproj", "SyncStreamAPI/"]
RUN dotnet restore "SyncStreamAPI/SyncStreamAPI.csproj"
COPY . .
WORKDIR "/src/SyncStreamAPI"
#RUN dotnet build "SyncStreamAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SyncStreamAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SyncStreamAPI.dll"]