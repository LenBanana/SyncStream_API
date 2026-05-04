FROM node:20-bookworm-slim AS base

# mediasoup requires build tools (Python 3, make, g++) for its native module.
# ffmpeg is required for server-side recording.
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 make g++ pkg-config ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# ── Install dependencies ──────────────────────────────────────────────────────
COPY package.json ./
# mediasoup's native build requires network access; use --ignore-scripts=false.
RUN npm install --ignore-scripts=false --production

# ── Copy source ───────────────────────────────────────────────────────────────
COPY server.js ./

# ── Runtime ───────────────────────────────────────────────────────────────────
EXPOSE 3000

# The RTC UDP/TCP port range is declared at the docker-compose level.
# Environment variables can override any config value at runtime.
ENV NODE_ENV=production

CMD ["node", "server.js"]
