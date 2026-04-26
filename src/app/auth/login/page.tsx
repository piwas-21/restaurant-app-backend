"use client";

import React, { useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import { Mail } from 'lucide-react';
import styles from "@/AuthPage.module.css";
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { login as loginUser, sendEmailVerification } from '@/authService';
import { useAuth } from '@/components/AuthContext';
import SocialLoginButtons from '@/components/auth/SocialLoginButtons';

export default function LoginPage() {
  const { t } = useTranslation();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [needsVerification, setNeedsVerification] = useState(false);
  const [resendLoading, setResendLoading] = useState(false);
  const [resendMessage, setResendMessage] = useState("");
  const emailInputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const { login } = useAuth();

  useEffect(() => {
    emailInputRef.current?.focus();
  }, []);

  const handleResendVerification = async () => {
    setResendLoading(true);
    setResendMessage("");

    try {
      const response = await sendEmailVerification({ email });
      if (response?.success || response?.succeeded) {
        setResendMessage(t('verification_email_resent', 'Verification email has been resent! Please check your inbox.'));
      } else {
        setResendMessage(t('resend_failed', 'Failed to resend email. Please try again later.'));
      }
    } catch (error) {
      setResendMessage(t('resend_error', 'An error occurred. Please try again.'));
    } finally {
      setResendLoading(false);
      setTimeout(() => setResendMessage(""), 5000);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setNeedsVerification(false);
    setResendMessage("");

    if (!email || !password) {
      setError(t('email_and_password_required', 'Email and password are required.'));
      return;
    }

    try {
      const response = await loginUser({ email, password });

      if (response.success) {
        login(response.data);
        const userRole = response.data.role.toLowerCase();

        switch (userRole) {
          case "admin":
            router.push("/admin/dashboard");
            break;
          case "customer":
            router.push("/account");
            break;
          case "cashier":
            router.push("/cashier");
            break;
          case "kitchenstaff":
            router.push("/kitchen-staff");
            break;
          case "server":
            router.push("/server");
            break;
          default:
            router.push("/");
            break;
        }
      } else {
        // Check if it's an email verification error
        if (response.message?.toLowerCase().includes('verify') ||
            response.message?.toLowerCase().includes('verification') ||
            response.errors?.[0]?.toLowerCase().includes('verify')) {
          setNeedsVerification(true);
          setError(response.errors?.[0] || response.message || t('email_verification_required', 'Please verify your email address before logging in.'));
        } else {
          setError(response.message || t('unknown_error', 'An unknown error occurred.'));
        }
      }
    } catch {
      setError(t('failed_to_connect_server', 'Failed to connect to the server.'));
    }
  };

  return (
    <main className={styles.authContainer}>
      <div className={styles.authForm} role="form" aria-labelledby="login-heading">
        <h1 id="login-heading">{t('login_page_title', 'Login')}</h1>
        <form onSubmit={handleSubmit} noValidate>
          {error && (
            <div className={styles.errorMessage} role="alert">
              <p>{error}</p>
              {needsVerification && (
                <div style={{ marginTop: '1rem' }}>
                  {resendMessage && (
                    <p style={{
                      color: resendMessage.includes('resent') ? '#10b981' : '#ef4444',
                      fontSize: '0.9rem',
                      marginBottom: '0.75rem'
                    }}>
                      {resendMessage}
                    </p>
                  )}
                  <button
                    type="button"
                    onClick={handleResendVerification}
                    disabled={resendLoading}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '0.5rem',
                      padding: '0.75rem 1.5rem',
                      backgroundColor: '#10b981',
                      color: 'white',
                      border: 'none',
                      borderRadius: '8px',
                      cursor: resendLoading ? 'not-allowed' : 'pointer',
                      opacity: resendLoading ? 0.6 : 1,
                      fontSize: '0.95rem',
                      fontWeight: 600,
                      transition: 'all 0.2s ease'
                    }}
                  >
                    <Mail size={18} />
                    {resendLoading ? t('sending', 'Sending...') : t('resend_verification_email', 'Resend Verification Email')}
                  </button>
                </div>
              )}
            </div>
          )}
          <div className={styles.formGroup}>
            <label htmlFor="email">{t('email', 'Email')}</label>
            <input
              type="email"
              id="email"
              name="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              aria-required="true"
              ref={emailInputRef}
              autoComplete="email"
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="password">{t('password_label', 'Password')}</label>
            <input
              type="password"
              id="password"
              name="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              aria-required="true"
              autoComplete="current-password"
            />
          </div>
          <button type="submit" className={styles.submitButton}>{t('login_button', 'Login')}</button>
        </form>
        <p className={styles.switchFormText}>
          {t('dont_have_account_auth', "Don't have an account?")} <Link href="/auth/register">{t('register_here', 'Register here')}</Link>
        </p>
        <SocialLoginButtons />
      </div>
    </main>
  );
}
