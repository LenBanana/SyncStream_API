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
# PUPPETEER RECIPE - Cross-platform (ARM64/AMD64)
#####################
# Install common dependencies first
RUN apt-get update && apt-get -y install --no-install-recommends \
    wget apt-utils python3 ca-certificates curl gnupg2 \
    fonts-ipafont-gothic fonts-wqy-zenhei fonts-thai-tlwg fonts-kacst

# Install architecture-specific browser
RUN if [ "$(dpkg --print-architecture)" = "amd64" ]; then \
    # AMD64: Install Google Chrome unstable from Google's repo \
    mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://dl-ssl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /etc/apt/keyrings/google.gpg \
    && echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/google.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y google-chrome-unstable --no-install-recommends \
    && export PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome-unstable \
    && echo "PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome-unstable" > /etc/environment; \
    else \
    # ARM64: Install Chromium from Debian repo \
    apt-get update \
    && apt-get install -y chromium --no-install-recommends \
    && export PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium \
    && echo "PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium" > /etc/environment; \
    fi \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Set default Puppeteer path that will be overridden by the above script at runtime
ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium