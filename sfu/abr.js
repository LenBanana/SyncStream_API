'use strict';

/**
 * Server-side Adaptive Bitrate (ABR) loop for mediasoup rooms.
 *
 * Every 2 seconds, each video consumer's score and producer score are checked.
 * Scores are 0–10 (mediasoup internal quality estimate).  Based on recent
 * history, the preferred spatial/temporal layer is stepped up or down so each
 * viewer independently gets the best quality their link can sustain.
 *
 * Rules:
 *   - Step DOWN immediately when consumer score ≤ 4 (sustained 1 sample enough)
 *   - Step UP after 5 consecutive good samples (score ≥ 8, producer ≥ 8)
 *   - Hysteresis: never auto-step above the layer last set by the UI (userMax)
 *   - Direction resets the other direction's counter
 */

const INTERVAL_MS   = 2000;
const UP_THRESHOLD  = 8;   // both consumer and producer score must be ≥ this
const DOWN_THRESHOLD = 4;  // consumer score ≤ this triggers immediate step-down
const UP_SAMPLES_NEEDED = 5; // consecutive good samples before stepping up

/** Per-consumer ABR state. */
class ConsumerState {
  constructor(maxSpatial, maxTemporal) {
    this.maxSpatial  = maxSpatial;   // highest layer available (from scalabilityMode / encodings)
    this.maxTemporal = maxTemporal;
    this.currentSpatial  = maxSpatial;   // layer we last requested
    this.currentTemporal = maxTemporal;
    this.userMaxSpatial  = maxSpatial;   // ceiling set by the UI (never auto-exceed)
    this.goodSamples = 0;
  }
}

/** Parse spatial / temporal layer count from a scalabilityMode string like 'L3T3_KEY'. */
function parseLayers(scalabilityMode) {
  if (!scalabilityMode) return null;
  const m = scalabilityMode.match(/^L(\d+)T(\d+)/);
  if (!m) return null;
  return {spatial: parseInt(m[1], 10) - 1, temporal: parseInt(m[2], 10) - 1};
}

/** Derive max spatial/temporal layers for a consumer's RTP encodings. */
function maxLayersForConsumer(consumer) {
  const encodings = consumer.rtpParameters.encodings ?? [];
  if (encodings.length === 0) return {spatial: 0, temporal: 0};
  // SVC: single encoding with scalabilityMode
  const firstMode = encodings[0].scalabilityMode;
  if (firstMode) {
    const parsed = parseLayers(firstMode);
    if (parsed) return parsed;
  }
  // Simulcast: one entry per spatial layer, temporal comes from individual scalabilityMode
  const temporal = parseLayers(encodings[0].scalabilityMode)?.temporal ?? 0;
  return {spatial: encodings.length - 1, temporal};
}

class RoomAbr {
  /**
   * @param {import('./server').Room} room
   * @param {(consumerId: string, spatial: number, temporal: number) => Promise<void>} applyFn
   */
  constructor(room, applyFn) {
    this.room    = room;
    this.apply   = applyFn;
    /** @type {Map<string, ConsumerState>} consumerId → state */
    this.states  = new Map();
    this.timer   = setInterval(() => this._tick().catch(console.error), INTERVAL_MS);
  }

  stop() {
    clearInterval(this.timer);
    this.states.clear();
  }

  /** Called by the UI preferred-layers endpoint so ABR never auto-exceeds user choice. */
  onUserSetLayers(consumerId, spatialLayer, temporalLayer) {
    const state = this.states.get(consumerId);
    if (state) {
      state.userMaxSpatial  = spatialLayer;
      state.currentSpatial  = spatialLayer;
      state.currentTemporal = temporalLayer;
      state.goodSamples     = 0;
    }
  }

  async _tick() {
    for (const [consumerId, consumer] of this.room.consumers) {
      if (consumer.kind !== 'video') continue;

      // Lazily initialise state on first tick.
      if (!this.states.has(consumerId)) {
        const {spatial, temporal} = maxLayersForConsumer(consumer);
        if (spatial === 0 && temporal === 0) continue; // single-layer, nothing to adapt
        this.states.set(consumerId, new ConsumerState(spatial, temporal));
      }
      const state = this.states.get(consumerId);

      // Get producer score to detect sender-side bottlenecks.
      const producerEntry = [...this.room.producers.values()]
        .find(e => e.producer.id === consumer.producerId);
      const producerScore = producerEntry?.producer.score ?? 10;
      const consumerScore = consumer.score ?? 10;

      const effectiveSpatialMax = Math.min(state.maxSpatial, state.userMaxSpatial);

      if (consumerScore <= DOWN_THRESHOLD) {
        // Immediate step down.
        state.goodSamples = 0;
        if (state.currentSpatial > 0) {
          state.currentSpatial  = Math.max(0, state.currentSpatial - 1);
          state.currentTemporal = state.maxTemporal;
          await this.apply(consumerId, state.currentSpatial, state.currentTemporal)
            .catch(() => {});
          console.log(`[ABR] consumer ${consumerId.slice(0, 8)} ↓ spatial → ${state.currentSpatial} (score ${consumerScore})`);
        }
      } else if (consumerScore >= UP_THRESHOLD && producerScore >= UP_THRESHOLD) {
        state.goodSamples++;
        if (state.goodSamples >= UP_SAMPLES_NEEDED && state.currentSpatial < effectiveSpatialMax) {
          state.goodSamples    = 0;
          state.currentSpatial = Math.min(effectiveSpatialMax, state.currentSpatial + 1);
          state.currentTemporal = state.maxTemporal;
          await this.apply(consumerId, state.currentSpatial, state.currentTemporal)
            .catch(() => {});
          console.log(`[ABR] consumer ${consumerId.slice(0, 8)} ↑ spatial → ${state.currentSpatial} (score ${consumerScore})`);
        }
      } else {
        // Neutral — reset up-counter only, don't step down.
        state.goodSamples = 0;
      }
    }

    // Prune state for consumers that have gone away.
    for (const consumerId of this.states.keys()) {
      if (!this.room.consumers.has(consumerId)) {
        this.states.delete(consumerId);
      }
    }
  }
}

module.exports = { RoomAbr };
