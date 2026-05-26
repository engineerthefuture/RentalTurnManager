'use strict';

/************************
 * Rental Turn Manager
 * MessagingLambda - intents/intent-detector.js
 *
 * Detects the intent of a platform message and extracts relevant data.
 * Returns a structured intent object consumed by the Lambda handler.
 *
 * Extensibility: add new intent types (checkout, inquiry, etc.) by
 * adding entries to INTENT_PATTERNS below.
 ***********************/

// Ordered list of intent patterns. First match wins.
const INTENT_PATTERNS = [
  {
    type: 'check-in',
    // Matches Airbnb's system message format: "[Name] has checked in." / "[Name] checked in."
    // Also handles "checked-in" (hyphenated) and minor variations.
    pattern: /([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(?:has\s+)?checked[\s-]in/,
    extractData: (match) => ({
      guestFirstName: match[1].split(' ')[0],
      guestFullName: match[1],
    }),
  },
];

class IntentDetector {
  /**
   * Analyze a message and return a detected intent.
   * @param {{ id: string, text: string, timestamp: Date }} message
   * @returns {{ type: string, [key: string]: any }}
   */
  detect(message) {
    const text = message.text || '';

    for (const { type, pattern, extractData } of INTENT_PATTERNS) {
      const match = text.match(pattern);
      if (match) {
        return { type, ...extractData(match) };
      }
    }

    return { type: 'unknown' };
  }
}

module.exports = { IntentDetector };
