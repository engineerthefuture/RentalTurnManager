'use strict';

/************************
 * Rental Turn Manager
 * MessagingLambda - index.js
 *
 * Lambda handler that scans rental platform inboxes for new guest messages,
 * detects intent (e.g. check-in notification), and sends automated replies.
 * Runs on a schedule via EventBridge. State is persisted in S3.
 *
 * Each inbox thread is tracked independently:
 *   - firstSeenAt     : when we first discovered the thread
 *   - lastCheckedAt   : last time we scanned it
 *   - lastMessageProcessedAt : cursor used as the "since" filter next run
 *   - replies         : full history of automated replies sent to this thread
 *
 * Author: Brent Foster
 * Created: 05-23-2026
 ***********************/

const chromium = require('@sparticuz/chromium-min');
const { AirbnbPlatform } = require('./platforms/airbnb');
const { IntentDetector } = require('./intents/intent-detector');
const { StateManager } = require('./state/state-manager');
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');

// Pinned Chromium release compatible with @sparticuz/chromium-min v131
const CHROMIUM_PACK_URL =
  'https://github.com/Sparticuz/chromium/releases/download/v131.0.1/chromium-v131.0.1-pack.tar';

exports.handler = async (event, context) => {
  console.log('MessagingLambda started');

  const stateManager = new StateManager({
    bucket: process.env.BOOKING_STATE_BUCKET,
    stateKey: 'message-responder/state.json',
    region: process.env.AWS_REGION,
  });

  // Global state only holds lastRunAt, used as the fallback "since" for
  // threads we've never seen before.
  const globalState = await stateManager.load();
  const lookbackMs = parseInt(process.env.LOOKBACK_MINUTES || '25', 10) * 60 * 1000;
  const defaultSince = globalState.lastRunAt
    ? new Date(globalState.lastRunAt)
    : new Date(Date.now() - lookbackMs);

  console.log(`Default lookback anchor: ${defaultSince.toISOString()}`);

  // Resolve Chromium executable once per invocation (cached in /tmp on warm starts)
  const executablePath = await chromium.executablePath(CHROMIUM_PACK_URL);

  const platformConfigs = [
    {
      name: 'airbnb',
      secretName: process.env.AIRBNB_SECRET_NAME,
      PlatformClass: AirbnbPlatform,
    },
    // Future: VRBO, Booking.com
  ];

  const intentDetector = new IntentDetector();
  let hasErrors = false;

  for (const { name, secretName, PlatformClass } of platformConfigs) {
    if (!secretName) {
      console.log(`Skipping ${name}: no secret configured`);
      continue;
    }

    const platform = new PlatformClass();
    let launched = false;

    try {
      const credentials = await getCredentials(secretName, process.env.AWS_REGION);

      if (!credentials.username || !credentials.password) {
        console.log(`Skipping ${name}: credentials are empty`);
        continue;
      }

      await platform.launch({
        executablePath,
        args: chromium.args,
        headless: chromium.headless,
      });
      launched = true;

      await platform.login(credentials);

      const threads = await platform.getInboxThreads();
      console.log(`[${name}] Found ${threads.length} thread(s) in inbox`);

      for (const thread of threads) {
        try {
          await processThread({
            platform,
            platformName: name,
            thread,
            stateManager,
            intentDetector,
            defaultSince,
          });
        } catch (threadErr) {
          console.error(`[${name}] Error processing thread ${thread.id}:`, threadErr.message);
          hasErrors = true;
        }
      }
    } catch (platformErr) {
      console.error(`[${name}] Platform error:`, platformErr.message);
      hasErrors = true;
    } finally {
      if (launched) {
        await platform.close().catch((err) =>
          console.error(`[${name}] Error closing browser:`, err.message)
        );
      }
    }
  }

  await stateManager.save({ lastRunAt: new Date().toISOString() });

  console.log(`MessagingLambda finished. Errors: ${hasErrors}`);
  return { success: !hasErrors, processedAt: new Date().toISOString() };
};

/**
 * Process a single inbox thread:
 *  1. Load its persisted state (or initialise a fresh record).
 *  2. Scan only messages newer than the stored cursor.
 *  3. Detect intents and send replies as needed.
 *  4. Persist updated thread state.
 */
async function processThread({ platform, platformName, thread, stateManager, intentDetector, defaultSince }) {
  const now = new Date();

  // Load existing thread state, or bootstrap a new record
  const existing = await stateManager.loadThread(platformName, thread.id);
  const threadState = existing ?? {
    id: thread.id,
    platform: platformName,
    firstSeenAt: now.toISOString(),
    lastCheckedAt: null,
    lastMessageProcessedAt: null,
    replies: [],
  };

  // Use the thread's own cursor; fall back to the global default for new threads
  const since = threadState.lastMessageProcessedAt
    ? new Date(threadState.lastMessageProcessedAt)
    : defaultSince;

  console.log(`[${platformName}] Thread ${thread.id}: scanning messages since ${since.toISOString()}`);

  const messages = await platform.getThreadMessages(thread.id, since);
  console.log(`[${platformName}] Thread ${thread.id}: ${messages.length} new message(s)`);

  for (const message of messages) {
    const intent = intentDetector.detect(message);

    if (intent.type === 'check-in') {
      const alreadyReplied = threadState.replies.some((r) => r.intent === 'check-in');
      if (alreadyReplied) {
        console.log(`[${platformName}] Thread ${thread.id}: check-in reply already sent, skipping`);
      } else {
        const guestFirstName = intent.guestFirstName || 'Guest';
        const reply = buildCheckInReply(guestFirstName);
        await platform.sendMessage(thread.id, reply);
        threadState.replies.push({
          intent: 'check-in',
          sentAt: now.toISOString(),
          message: reply,
          guestFirstName,
        });
        console.log(`[${platformName}] Thread ${thread.id}: sent check-in reply to ${guestFirstName}`);
      }
      break; // One intent action per thread per run
    }
  }

  // Advance the cursor to now so we don't re-scan these messages next run.
  // Only advance if we actually found messages (preserves cursor on empty runs).
  if (messages.length > 0) {
    threadState.lastMessageProcessedAt = now.toISOString();
  }
  threadState.lastCheckedAt = now.toISOString();

  await stateManager.saveThread(platformName, thread.id, threadState);
}

function buildCheckInReply(guestFirstName) {
  return `Hi ${guestFirstName}, please enjoy your stay, and don't hesitate to reach out if you need anything. Thanks!`;
}

async function getCredentials(secretName, region) {
  const client = new SecretsManagerClient({ region });
  const response = await client.send(new GetSecretValueCommand({ SecretId: secretName }));
  return JSON.parse(response.SecretString);
}
