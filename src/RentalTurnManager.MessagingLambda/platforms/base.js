'use strict';

/************************
 * Rental Turn Manager
 * MessagingLambda - platforms/base.js
 *
 * Abstract base class for rental platform messaging automation.
 * Each platform (Airbnb, VRBO, Booking.com) extends this class
 * and provides platform-specific implementations.
 ***********************/

class BasePlatform {
  /** @returns {string} Platform identifier, e.g. 'airbnb' */
  get name() {
    throw new Error(`${this.constructor.name} must implement get name()`);
  }

  /**
   * Launch a headless browser instance.
   * @param {{ executablePath: string, args: string[], headless: boolean }} options
   */
  async launch(options) {
    throw new Error(`${this.constructor.name} must implement launch()`);
  }

  /**
   * Log into the platform with the provided credentials.
   * @param {{ username: string, password: string }} credentials
   */
  async login(credentials) {
    throw new Error(`${this.constructor.name} must implement login()`);
  }

  /**
   * Return all visible inbox threads.
   * @returns {Promise<Array<{ id: string }>>}
   */
  async getInboxThreads() {
    throw new Error(`${this.constructor.name} must implement getInboxThreads()`);
  }

  /**
   * Return messages in a thread that are newer than `since`.
   * @param {string} threadId
   * @param {Date} since
   * @returns {Promise<Array<{ id: string, text: string, timestamp: Date }>>}
   */
  async getThreadMessages(threadId, since) {
    throw new Error(`${this.constructor.name} must implement getThreadMessages()`);
  }

  /**
   * Send a message in a thread.
   * @param {string} threadId
   * @param {string} message
   */
  async sendMessage(threadId, message) {
    throw new Error(`${this.constructor.name} must implement sendMessage()`);
  }

  /** Close the browser and release resources. */
  async close() {
    throw new Error(`${this.constructor.name} must implement close()`);
  }
}

module.exports = { BasePlatform };
