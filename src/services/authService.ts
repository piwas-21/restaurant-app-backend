import { z } from 'zod';
import { loginSchema } from '../schemas/auth.schema';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL;
const AUTH_API_URL = `${API_BASE_URL}/api/Auth`;

/**
 * Get session ID from localStorage for basket merge on login
 */
function getSessionId(): string | null {
  if (typeof window === 'undefined') return null;
  return localStorage.getItem('rumi_session_id');
}

export async function login(formData: z.infer<typeof loginSchema>) {
  const sessionId = getSessionId();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (sessionId) {
    headers['X-Session-Id'] = sessionId;
  }

  const response = await fetch(`${AUTH_API_URL}/login`, {
    method: 'POST',
    headers,
    body: JSON.stringify(formData),
  });

  if (response.ok) {
    const data = await response.json();
    if (data.success) {
      localStorage.setItem('auth_token', data.data.accessToken);
      localStorage.setItem('refresh_token', data.data.refreshToken);
    }
    return data;
  }

  return response.json();
}

export async function refreshToken() {
    const accessToken = localStorage.getItem('auth_token');
    const refreshToken = localStorage.getItem('refresh_token');

    try {
        const response = await fetch(`${AUTH_API_URL}/refresh-token`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({ accessToken, refreshToken }),
        });

        const data = await response.json();
        if (data.success) {
            localStorage.setItem('auth_token', data.data.accessToken);
            localStorage.setItem('refresh_token', data.data.refreshToken);
        }
        return data;
    } catch (error) {
        // Network error or other failure
        return { success: false, message: 'Failed to refresh token' };
    }
}

// Customer registration
export type CustomerRegistrationPayload = {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  confirmPassword: string;
};

export async function registerCustomer(formData: CustomerRegistrationPayload) {
  const response = await fetch(`${API_BASE_URL}/api/User/register/customer`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      accept: 'text/plain',
    },
    body: JSON.stringify(formData),
  });

  const data = await response.json();
  if (response.ok && data?.success) {
    try {
      if (data.data?.accessToken) localStorage.setItem('auth_token', data.data.accessToken);
      if (data.data?.refreshToken) localStorage.setItem('refresh_token', data.data.refreshToken);
    } catch (e) {
      // localStorage may be unavailable; ignore and let caller proceed
      // eslint-disable-next-line no-console
      console.warn('Could not persist tokens to localStorage', e);
    }
  }
  return data;
}

/**
 * Forgot Password - Request password reset
 */
export interface ForgotPasswordCommand {
  email: string;
}

export async function forgotPassword(formData: ForgotPasswordCommand) {
  const response = await fetch(`${AUTH_API_URL}/forgot-password`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });

  return response.json();
}

/**
 * Reset Password - Reset password with token
 */
export interface ResetPasswordCommand {
  email: string;
  token: string;
  newPassword: string;
  confirmPassword: string;
}

export async function resetPassword(formData: ResetPasswordCommand) {
  const response = await fetch(`${AUTH_API_URL}/reset-password`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });

  return response.json();
}

/**
 * Change Password - Change password for authenticated user
 */
export interface ChangePasswordCommand {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export async function changePassword(formData: ChangePasswordCommand) {
  const token = localStorage.getItem('auth_token');

  const response = await fetch(`${AUTH_API_URL}/change-password`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
    },
    body: JSON.stringify(formData),
  });

  const data = await response.json();

  if (!response.ok) {
    throw new Error(data.message || 'Failed to change password');
  }

  return data;
}

/**
 * Send Email Verification
 */
export interface SendEmailVerificationCommand {
  email: string;
}

export async function sendEmailVerification(formData: SendEmailVerificationCommand) {
  const response = await fetch(`${AUTH_API_URL}/send-email-verification`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });

  return response.json();
}

/**
 * Verify Email - Verify email address with token
 */
export interface VerifyEmailCommand {
  email: string;
  token: string;
}

export async function verifyEmail(formData: VerifyEmailCommand) {
  const response = await fetch(`${AUTH_API_URL}/verify-email`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });

  return response.json();
}

export async function requestAccountDeletion() {
  const token = localStorage.getItem("auth_token");
  const response = await fetch(`${API_BASE_URL}/api/User/request-deletion`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${token}`
    },
  });
  return response.json();
}

export async function confirmAccountDeletion(data: { userId: string; token: string }) {
  const response = await fetch(`${API_BASE_URL}/api/User/confirm-deletion`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(data),
  });
  return response.json();
}

export async function googleLogin(idToken: string) {
  const sessionId = getSessionId();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (sessionId) {
    headers['X-Session-Id'] = sessionId;
  }

  const response = await fetch(`${AUTH_API_URL}/google-login`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ idToken }),
  });

  const data = await response.json();
  if (response.ok && data?.success) {
    try {
      if (data.data?.accessToken) localStorage.setItem('auth_token', data.data.accessToken);
      if (data.data?.refreshToken) localStorage.setItem('refresh_token', data.data.refreshToken);
    } catch (e) {
      console.warn('Could not persist tokens to localStorage', e);
    }
  }
  return data;
}

export async function appleLogin(idToken: string, user?: { firstName: string; lastName: string }) {
  const sessionId = getSessionId();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (sessionId) {
    headers['X-Session-Id'] = sessionId;
  }

  const response = await fetch(`${AUTH_API_URL}/apple-login`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
        idToken,
        firstName: user?.firstName,
        lastName: user?.lastName
    }),
  });

  const data = await response.json();
  if (response.ok && data?.success) {
    try {
      if (data.data?.accessToken) localStorage.setItem('auth_token', data.data.accessToken);
      if (data.data?.refreshToken) localStorage.setItem('refresh_token', data.data.refreshToken);
    } catch (e) {
      console.warn('Could not persist tokens to localStorage', e);
    }
  }
  return data;
}
