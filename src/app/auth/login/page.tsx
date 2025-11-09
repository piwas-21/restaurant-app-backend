"use client";

import React, { useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import styles from "@/AuthPage.module.css";
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { login as loginUser } from '@/authService';
import { useAuth } from '@/components/AuthContext';

export default function LoginPage() {
  const { t } = useTranslation();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const emailInputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const { login } = useAuth();

  useEffect(() => {
    emailInputRef.current?.focus();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

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
          case "kitchen-staff":
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
        setError(response.message || t('unknown_error', 'An unknown error occurred.'));
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
          {error && <p className={styles.errorMessage} role="alert">{error}</p>}
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
      </div>
    </main>
  );
}
