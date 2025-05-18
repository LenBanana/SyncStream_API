FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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
RUN dotnet publish "SyncStreamAPI.csproj" -c Release -p:DefineConstants=LINUX -o /app/publish
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SyncStreamAPI.dll"]

#####################
#PUPPETEER RECIPE - ARM64 Compatible
#####################
# Install latest chrome dev package and fonts to support major charsets (Chinese, Japanese, Arabic, Hebrew, Thai and a few others)
# Note: this installs the necessary libs to make the bundled version of Chromium that Puppeteer
# installs, work.
RUN apt-get update && apt-get -f install && apt-get -y install wget gnupg2 apt-utils python3

# Architecture-aware Chrome installation
RUN apt-get update \
    && apt-get install -y ca-certificates curl gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://dl-ssl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /etc/apt/keyrings/google.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/google.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y google-chrome-unstable fonts-ipafont-gothic fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst \
      --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*
#####################
#END PUPPETEER RECIPE
#####################
ENV PUPPETEER_EXECUTABLE_PATH "/usr/bin/google-chrome-unstable"