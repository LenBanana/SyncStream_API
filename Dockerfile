#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /app
EXPOSE 80
EXPOSE 443
#RUN apt-get update && apt-get install -y libx11-6 libx11-xcb1 libatk1.0-0 libgtk-3-0 libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2 libxshmfence1 libnss3

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