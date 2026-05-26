'use strict';

/************************
 * Rental Turn Manager
 * MessagingLambda - platforms/airbnb.js
 *
 * Airbnb-specific implementation of BasePlatform. Uses Playwright to
 * automate the Airbnb hosting inbox at airbnb.com/hosting/inbox.
 *
 * NOTE: Airbnb does not expose a public messaging API. This automation
 * relies on their web interface and may require selector updates if
 * Airbnb changes their UI. Selectors prioritize stable attributes
 * (aria roles, href patterns, time[datetime]) over volatile class names.
 ***********************/

const { chromium } = require('playwright-core');
const { BasePlatform } = require('./base');

const BASE_URL = 'https://www.airbnb.com';
const LOGIN_URL = `${BASE_URL}/login`;
const INBOX_URL = `${BASE_URL}/hosting/inbox`;

class AirbnbPlatform extends BasePlatform {
  get name() {
    return 'airbnb';
  }

  async launch({ executablePath, args, headless }) {
    this._browser = await chromium.launch({ executablePath, args, headless });
    this._context = await this._browser.newContext({
      userAgent:
        'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
      viewport: { width: 1280, height: 900 },
      locale: 'en-US',
    });
    this._page = await this._context.newPage();
  }

  async login(credentials) {
    const page = this._page;
    console.log('[airbnb] Navigating to login page...');
    await page.goto(LOGIN_URL, { waitUntil: 'domcontentloaded', timeout: 60_000 });

    // Dismiss cookie consent if present
    await page
      .getByRole('button', { name: /accept/i })
      .click({ timeout: 3_000 })
      .catch(() => {});

    // Airbnb shows social login buttons plus "Continue with email" as a secondary option
    const emailButton = page.getByRole('button', { name: /continue with email/i });
    const hasEmailButton = await emailButton.isVisible({ timeout: 8_000 }).catch(() => false);
    if (hasEmailButton) {
      await emailButton.click();
    }

    // Fill email and advance
    const emailInput = page.getByRole('textbox', { name: /email/i });
    await emailInput.waitFor({ timeout: 15_000 });
    await emailInput.fill(credentials.username);
    await page.getByRole('button', { name: /continue/i }).click();

    // Fill password and submit
    const passwordInput = page.getByRole('textbox', { name: /password/i });
    await passwordInput.waitFor({ timeout: 15_000 });
    await passwordInput.fill(credentials.password);
    await page.getByRole('button', { name: /log in/i }).click();

    // Wait for redirect away from the login page
    await page.waitForURL((url) => !url.toString().includes('/login'), { timeout: 30_000 });
    console.log('[airbnb] Login successful');
  }

  async getInboxThreads() {
    const page = this._page;
    console.log('[airbnb] Loading hosting inbox...');
    await page.goto(INBOX_URL, { waitUntil: 'networkidle', timeout: 60_000 });

    // Thread links always follow the pattern /hosting/inbox/<numeric-id>
    // This href pattern is far more stable than any class-based selector.
    await page
      .locator('a[href*="/hosting/inbox/"]')
      .first()
      .waitFor({ timeout: 30_000 })
      .catch(() => console.log('[airbnb] Warning: no thread links found within timeout'));

    const links = await page.locator('a[href*="/hosting/inbox/"]').all();
    const seen = new Set();
    const threads = [];

    for (const link of links) {
      const href = await link.getAttribute('href').catch(() => null);
      const match = href?.match(/\/hosting\/inbox\/(\d+)/);
      if (match && !seen.has(match[1])) {
        seen.add(match[1]);
        threads.push({ id: match[1] });
      }
    }

    console.log(`[airbnb] Found ${threads.length} thread(s)`);
    return threads;
  }

  async getThreadMessages(threadId, since) {
    const page = this._page;
    await page.goto(`${INBOX_URL}/${threadId}`, { waitUntil: 'networkidle', timeout: 60_000 });

    // Brief wait for the conversation to fully render
    await page.waitForTimeout(2_000);

    const messages = [];

    // --- Strategy 1: look for <time datetime="..."> elements near message text ---
    // Airbnb renders timestamps as <time> elements with ISO datetime attributes.
    const timeElements = await page.locator('time[datetime]').all();
    for (const timeEl of timeElements) {
      try {
        const datetime = await timeEl.getAttribute('datetime');
        if (!datetime) continue;
        const msgDate = new Date(datetime);
        if (since && msgDate <= since) continue;

        // Closest ancestor that contains the actual message text
        const container = timeEl.locator('xpath=ancestor::*[contains(@class,"message") or contains(@class,"Message")][1]');
        const text = await container.textContent({ timeout: 2_000 }).catch(
          () => page.locator('main').textContent({ timeout: 2_000 }).catch(() => '')
        );

        messages.push({
          id: `${threadId}-${datetime}`,
          text: text?.trim() ?? '',
          timestamp: msgDate,
        });
      } catch {
        // Skip malformed time elements
      }
    }

    // --- Strategy 2: fallback full-text scan (no reliable timestamps available) ---
    // If strategy 1 yielded nothing, read the whole conversation area and let the
    // IntentDetector look for check-in patterns. The processedCheckIns state map
    // prevents duplicate replies across runs.
    if (messages.length === 0) {
      console.log(`[airbnb] Thread ${threadId}: falling back to full-text scan`);
      const pageText = await page
        .locator('main')
        .textContent({ timeout: 10_000 })
        .catch(() => page.textContent('body').catch(() => ''));

      if (pageText) {
        messages.push({
          id: `${threadId}-fulltext`,
          text: pageText.trim(),
          timestamp: new Date(),
        });
      }
    }

    return messages;
  }

  async sendMessage(threadId, message) {
    const page = this._page;

    // Navigate to the thread if not already there
    if (!page.url().includes(`/inbox/${threadId}`)) {
      await page.goto(`${INBOX_URL}/${threadId}`, { waitUntil: 'networkidle', timeout: 60_000 });
    }

    // Airbnb uses a contenteditable div or a textarea for message composition.
    // Try the most common selectors in priority order.
    const inputLocator = page
      .locator('[contenteditable="true"]')
      .last()
      .or(page.getByRole('textbox', { name: /message|reply|write/i }))
      .or(page.locator('textarea').last());

    await inputLocator.waitFor({ timeout: 15_000 });
    await inputLocator.click();
    await inputLocator.fill(message);

    // Try an explicit Send button first; fall back to Enter key
    const sendButton = page.getByRole('button', { name: /^send$/i });
    const hasSendButton = await sendButton.isVisible({ timeout: 2_000 }).catch(() => false);
    if (hasSendButton) {
      await sendButton.click();
    } else {
      await page.keyboard.press('Enter');
    }

    // Brief pause to allow the send request to complete before we close the browser
    await page.waitForTimeout(3_000);
    console.log(`[airbnb] Message sent to thread ${threadId}`);
  }

  async close() {
    await this._context?.close().catch(() => {});
    await this._browser?.close().catch(() => {});
  }
}

module.exports = { AirbnbPlatform };
