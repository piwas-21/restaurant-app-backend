"use client";

import React, { useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import styles from "../../styles/AuthPage.module.css";
import { useRouter } from 'next/navigation';
import { z } from 'zod';
import { registerCustomer, customerRegistrationSchema } from '../../../lib/auth/utils';

export default function RegisterPage() {
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const firstNameInputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();

  useEffect(() => {
    firstNameInputRef.current?.focus();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    try {
      const formData = { firstName, lastName, email, password, confirmPassword };
      customerRegistrationSchema.parse(formData);

      const response = await registerCustomer(formData);

      if (response.success) {
        setSuccess(response.message);
        setTimeout(() => {
          router.push('/auth/login');
        }, 2000);
      } else {
        setError(response.message || "Failed to register.");
      }
    } catch (error) {
      if (error instanceof z.ZodError) {
        setError(error.errors.map(e => e.message).join(", "));
      } else {
        setError("An unknown error occurred.");
      }
    }
  };

  return (
    <main className={styles.authContainer}>
      <div className={styles.authForm} role="form" aria-labelledby="register-heading">
        <h1 id="register-heading">Register</h1>
        <form onSubmit={handleSubmit} noValidate>
          {error && <p className={styles.errorMessage} role="alert">{error}</p>}
          {success && <p className="successMessage" role="alert">{success}</p>}
          <div className={styles.formGroup}>
            <label htmlFor="firstName">First Name</label>
            <input
              type="text"
              id="firstName"
              name="firstName"
              value={firstName}
              onChange={(e) => setFirstName(e.target.value)}
              required
              ref={firstNameInputRef}
              autoComplete="given-name"
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="lastName">Last Name</label>
            <input
              type="text"
              id="lastName"
              name="lastName"
              value={lastName}
              onChange={(e) => setLastName(e.target.value)}
              required
              autoComplete="family-name"
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="email">Email</label>
            <input
              type="email"
              id="email"
              name="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoComplete="email"
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="password">Password</label>
            <input
              type="password"
              id="password"
              name="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              autoComplete="new-password"
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="confirmPassword">Confirm Password</label>
            <input
              type="password"
              id="confirmPassword"
              name="confirmPassword"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              required
              autoComplete="new-password"
            />
          </div>
          <button type="submit" className={styles.submitButton}>Register</button>
        </form>
        <p className={styles.switchFormText}>
          Already have an account? <Link href="/auth/login">Login here</Link>
        </p>
      </div>
    </main>
  );
}
