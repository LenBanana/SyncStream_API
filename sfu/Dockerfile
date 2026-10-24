FROM node:20-bookworm-slim AS base

# mediasoup requires build tools (Python 3, make, g++) for its native module.
# ffmpeg is required for server-side file streaming and recording.
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 make g++ pkg-config ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# ── Install dependencies ──────────────────────────────────────────────────────
# Installed from the lockfile so every build runs the mediasoup version that was tested.
# mediasoup's install script fetches or builds its worker binary, so scripts must run.
COPY package.json package-lock.json ./
RUN npm ci --omit=dev

# ── Copy source ───────────────────────────────────────────────────────────────
COPY server.js ./
COPY server-file-streamer.js ./

# ── Runtime ───────────────────────────────────────────────────────────────────
EXPOSE 3000

# The WebRtcServer ports are declared at the docker-compose level.
# Environment variables can override any config value at runtime.
ENV NODE_ENV=production

CMD ["node", "server.js"]
