/**
 * Session Service
 *
 * Manages session IDs for anonymous users to track their baskets.
 * Session IDs are stored in localStorage and sent to the backend via X-Session-Id header.
 */

const SESSION_ID_KEY = 'rumi_session_id';
const SESSION_EXPIRY_KEY = 'rumi_session_expiry';
const SESSION_DURATION_DAYS = 7; // Session expires after 7 days

/**
 * Generate a new UUID v4
 * Simple implementation without external dependencies
 */
function generateUUID(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

/**
 * Check if session has expired
 */
function isSessionExpired(expiryDate: string | null): boolean {
  if (!expiryDate) return true;

  try {
    const expiry = new Date(expiryDate);
    const now = new Date();
    return now >= expiry;
  } catch {
    return true;
  }
}

/**
 * Calculate session expiry date
 */
function getSessionExpiry(): string {
  const expiry = new Date();
  expiry.setDate(expiry.getDate() + SESSION_DURATION_DAYS);
  return expiry.toISOString();
}

/**
 * Get current session ID from localStorage
 * Returns null if session doesn't exist or has expired
 */
export function getSessionId(): string | null {
  if (typeof window === 'undefined') return null;

  try {
    const sessionId = localStorage.getItem(SESSION_ID_KEY);
    const expiryDate = localStorage.getItem(SESSION_EXPIRY_KEY);

    // Check if session exists and hasn't expired
    if (sessionId && !isSessionExpired(expiryDate)) {
      return sessionId;
    }

    // Session expired or doesn't exist
    return null;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error reading session ID from localStorage:', error);
    return null;
  }
}

/**
 * Create a new session ID and store it
 * Returns the newly created session ID
 */
export function createSessionId(): string {
  if (typeof window === 'undefined') {
    throw new Error('Cannot create session ID on server side');
  }

  try {
    const sessionId = generateUUID();
    const expiryDate = getSessionExpiry();

    localStorage.setItem(SESSION_ID_KEY, sessionId);
    localStorage.setItem(SESSION_EXPIRY_KEY, expiryDate);

    return sessionId;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error creating session ID:', error);
    throw new Error('Failed to create session ID');
  }
}

/**
 * Get or create session ID
 * Returns existing session if valid, otherwise creates a new one
 */
export function getOrCreateSessionId(): string {
  const existingSession = getSessionId();

  if (existingSession) {
    return existingSession;
  }

  return createSessionId();
}

/**
 * Clear session ID from localStorage
 * Used when user logs in (basket will be merged with user account)
 */
export function clearSessionId(): void {
  if (typeof window === 'undefined') return;

  try {
    localStorage.removeItem(SESSION_ID_KEY);
    localStorage.removeItem(SESSION_EXPIRY_KEY);
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error clearing session ID:', error);
  }
}

/**
 * Refresh session expiry (extend session lifetime)
 * Call this on user activity to keep session alive
 */
export function refreshSessionExpiry(): void {
  if (typeof window === 'undefined') return;

  const sessionId = getSessionId();
  if (!sessionId) return;

  try {
    const newExpiryDate = getSessionExpiry();
    localStorage.setItem(SESSION_EXPIRY_KEY, newExpiryDate);
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error refreshing session expiry:', error);
  }
}

/**
 * Check if user has an active session
 */
export function hasActiveSession(): boolean {
  return getSessionId() !== null;
}

/**
 * Get session expiry date
 */
export function getSessionExpiryDate(): Date | null {
  if (typeof window === 'undefined') return null;

  try {
    const expiryDate = localStorage.getItem(SESSION_EXPIRY_KEY);
    if (!expiryDate) return null;

    return new Date(expiryDate);
  } catch {
    return null;
  }
}

/**
 * Session service object with all methods
 */
export const sessionService = {
  getSessionId,
  createSessionId,
  getOrCreateSessionId,
  clearSessionId,
  refreshSessionExpiry,
  hasActiveSession,
  getSessionExpiryDate,
};
