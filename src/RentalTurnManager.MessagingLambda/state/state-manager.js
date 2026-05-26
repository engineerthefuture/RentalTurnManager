'use strict';

/************************
 * Rental Turn Manager
 * MessagingLambda - state/state-manager.js
 *
 * Persists message-responder state to S3 using two levels:
 *
 *   Global run state
 *   ─────────────────
 *   message-responder/state.json
 *   { "lastRunAt": "<ISO>" }
 *
 *   Per-thread state (one file per conversation per platform)
 *   ──────────────────────────────────────────────────────────
 *   message-responder/threads/<platform>/<threadId>.json
 *   {
 *     "id": "<threadId>",
 *     "platform": "<platform>",
 *     "firstSeenAt": "<ISO>",
 *     "lastCheckedAt": "<ISO>",
 *     "lastMessageProcessedAt": "<ISO>",
 *     "replies": [
 *       { "intent": "check-in", "sentAt": "<ISO>", "message": "...", "guestFirstName": "..." }
 *     ]
 *   }
 ***********************/

const { S3Client, GetObjectCommand, PutObjectCommand } = require('@aws-sdk/client-s3');

const THREAD_PREFIX = 'message-responder/threads';

class StateManager {
  /**
   * @param {{ bucket: string, stateKey: string, region: string }} options
   */
  constructor({ bucket, stateKey, region }) {
    this._bucket = bucket;
    this._stateKey = stateKey;
    this._client = new S3Client({ region });
  }

  // ── Global run state ────────────────────────────────────────────────────────

  /** Load global run state from S3. Returns empty object if not yet created. */
  async load() {
    return this._getJson(this._stateKey, {});
  }

  /** Persist global run state to S3. */
  async save(state) {
    await this._putJson(this._stateKey, state);
    console.log(`[state] Global state saved (lastRunAt: ${state.lastRunAt})`);
  }

  // ── Per-thread state ─────────────────────────────────────────────────────────

  /**
   * Load state for a single thread. Returns null if this thread has never been seen.
   * @param {string} platform  e.g. 'airbnb'
   * @param {string} threadId
   * @returns {Promise<object|null>}
   */
  async loadThread(platform, threadId) {
    const key = this._threadKey(platform, threadId);
    return this._getJson(key, null);
  }

  /**
   * Persist state for a single thread.
   * @param {string} platform
   * @param {string} threadId
   * @param {object} threadState
   */
  async saveThread(platform, threadId, threadState) {
    const key = this._threadKey(platform, threadId);
    await this._putJson(key, threadState);
    console.log(`[state] Thread state saved: ${key}`);
  }

  // ── Helpers ──────────────────────────────────────────────────────────────────

  _threadKey(platform, threadId) {
    return `${THREAD_PREFIX}/${platform}/${threadId}.json`;
  }

  async _getJson(key, defaultValue) {
    try {
      const response = await this._client.send(
        new GetObjectCommand({ Bucket: this._bucket, Key: key })
      );
      const body = await response.Body.transformToString('utf-8');
      return JSON.parse(body);
    } catch (err) {
      if (err.name === 'NoSuchKey') return defaultValue;
      throw err;
    }
  }

  async _putJson(key, value) {
    await this._client.send(
      new PutObjectCommand({
        Bucket: this._bucket,
        Key: key,
        Body: JSON.stringify(value, null, 2),
        ContentType: 'application/json',
      })
    );
  }
}

module.exports = { StateManager };
