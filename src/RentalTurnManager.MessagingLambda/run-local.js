'use strict';

/************************
 * Local test runner for the MessagingLambda browser automation.
 *
 * Bypasses Lambda, Secrets Manager, S3, and the @sparticuz/chromium-min
 * binary download. Uses local Chrome/Chromium and file-based state so
 * you can iterate on selector and parsing issues without deploying.
 *
 * Usage:
 *   node run-local.js              # headed browser, no messages sent
 *   node run-local.js --headless   # headless mode
 *   node run-local.js --send       # actually send replies (default is dry-run)
 *
 * Prerequisites:
 *   1. Fill in credentials.local.json with real Airbnb credentials.
 *   2. Google Chrome installed at the standard macOS path, or set CHROME_PATH.
 ***********************/

const path = require('path');
const fs = require('fs');
const { AirbnbPlatform } = require('./platforms/airbnb');
const { IntentDetector } = require('./intents/intent-detector');

// ── CLI flags ─────────────────────────────────────────────────────────────────

const args = process.argv.slice(2);
const headless = args.includes('--headless');
const dryRun = !args.includes('--send');

// ── Credentials ───────────────────────────────────────────────────────────────

const credPath = path.join(__dirname, 'credentials.local.json');
if (!fs.existsSync(credPath)) {
  console.error('Missing credentials.local.json — create it with { "username": "...", "password": "..." }');
  process.exit(1);
}
const credentials = JSON.parse(fs.readFileSync(credPath, 'utf-8'));

if (!credentials.username || credentials.username.includes('example.com')) {
  console.error('credentials.local.json still contains placeholder values — fill in real credentials');
  process.exit(1);
}

// ── Local file-based state ────────────────────────────────────────────────────

const STATE_DIR = path.join(__dirname, '.local-state');
fs.mkdirSync(STATE_DIR, { recursive: true });

class LocalStateManager {
  load() {
    const p = path.join(STATE_DIR, 'state.json');
    return fs.existsSync(p) ? JSON.parse(fs.readFileSync(p, 'utf-8')) : {};
  }
  save(state) {
    fs.writeFileSync(path.join(STATE_DIR, 'state.json'), JSON.stringify(state, null, 2));
  }
  loadThread(platform, threadId) {
    const p = path.join(STATE_DIR, `${platform}-${threadId}.json`);
    return fs.existsSync(p) ? JSON.parse(fs.readFileSync(p, 'utf-8')) : null;
  }
  saveThread(platform, threadId, threadState) {
    fs.writeFileSync(
      path.join(STATE_DIR, `${platform}-${threadId}.json`),
      JSON.stringify(threadState, null, 2)
    );
    console.log(`[state] Saved: ${platform}-${threadId}.json`);
  }
}

// ── Browser resolution ────────────────────────────────────────────────────────

function findChrome() {
  if (process.env.CHROME_PATH && fs.existsSync(process.env.CHROME_PATH)) {
    return process.env.CHROME_PATH;
  }
  const candidates = [
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Chromium.app/Contents/MacOS/Chromium',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium-browser',
  ];
  return candidates.find((p) => fs.existsSync(p));
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  console.log(`[local] headless=${headless}  dryRun=${dryRun}`);
  if (dryRun) console.log('[local] Dry-run mode: no messages will be sent (pass --send to enable)');

  const executablePath = findChrome();
  if (executablePath) {
    console.log(`[local] Browser: ${executablePath}`);
  } else {
    console.error('[local] No browser found. Install Chrome or set CHROME_PATH.');
    process.exit(1);
  }

  const stateManager = new LocalStateManager();
  const globalState = stateManager.load();
  const lookbackMs = 25 * 60 * 1000;
  const defaultSince = globalState.lastRunAt
    ? new Date(globalState.lastRunAt)
    : new Date(Date.now() - lookbackMs);

  console.log(`[local] Lookback anchor: ${defaultSince.toISOString()}`);

  const platform = new AirbnbPlatform();
  const intentDetector = new IntentDetector();

  try {
    await platform.launch({ executablePath, args: [], headless });
    await platform.login(credentials);

    const threads = await platform.getInboxThreads();
    console.log(`[airbnb] Found ${threads.length} thread(s)`);

    for (const thread of threads) {
      try {
        await processThread({ platform, thread, stateManager, intentDetector, defaultSince, dryRun });
      } catch (err) {
        console.error(`[airbnb] Thread ${thread.id} error:`, err.message);
        console.error(err.stack);
      }
    }
  } finally {
    await platform.close().catch((err) => console.error('[local] Close error:', err.message));
  }

  stateManager.save({ lastRunAt: new Date().toISOString() });
  console.log('[local] Done');
}

// ── Thread processing ─────────────────────────────────────────────────────────

async function processThread({ platform, thread, stateManager, intentDetector, defaultSince, dryRun }) {
  const platformName = 'airbnb';
  const now = new Date();

  const existing = stateManager.loadThread(platformName, thread.id);
  const threadState = existing ?? {
    id: thread.id,
    platform: platformName,
    firstSeenAt: now.toISOString(),
    lastCheckedAt: null,
    lastMessageProcessedAt: null,
    replies: [],
  };

  const since = threadState.lastMessageProcessedAt
    ? new Date(threadState.lastMessageProcessedAt)
    : defaultSince;

  console.log(`\n[airbnb] ── Thread ${thread.id} (since ${since.toISOString()}) ──`);

  const messages = await platform.getThreadMessages(thread.id, since);
  console.log(`[airbnb] Thread ${thread.id}: ${messages.length} message(s) found`);

  for (const message of messages) {
    const intent = intentDetector.detect(message);
    console.log(`[airbnb] Thread ${thread.id}: intent="${intent.type}" | text="${message.text?.slice(0, 80)}"`);

    if (intent.type === 'check-in') {
      const alreadyReplied = threadState.replies.some((r) => r.intent === 'check-in');
      if (alreadyReplied) {
        console.log(`[airbnb] Thread ${thread.id}: check-in reply already sent — skipping`);
      } else {
        const guestFirstName = intent.guestFirstName || 'Guest';
        const reply = `Hi ${guestFirstName}, please enjoy your stay, and don't hesitate to reach out if you need anything. Thanks!`;
        if (dryRun) {
          console.log(`[airbnb] Thread ${thread.id}: [DRY RUN] would send → "${reply}"`);
        } else {
          await platform.sendMessage(thread.id, reply);
          threadState.replies.push({ intent: 'check-in', sentAt: now.toISOString(), message: reply, guestFirstName });
          console.log(`[airbnb] Thread ${thread.id}: sent check-in reply to ${guestFirstName}`);
        }
      }
      break;
    }
  }

  if (messages.length > 0) threadState.lastMessageProcessedAt = now.toISOString();
  threadState.lastCheckedAt = now.toISOString();
  stateManager.saveThread(platformName, thread.id, threadState);
}

main().catch((err) => {
  console.error('[local] Fatal:', err);
  process.exit(1);
});
