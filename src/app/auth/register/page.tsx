'use client';

import React, { useState, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import Link from 'next/link';
import { Mail, CheckCircle, ArrowRight } from 'lucide-react';
import styles from '@/AuthPage.module.css';
import successStyles from './RegisterSuccess.module.css';
import { useRouter } from 'next/navigation';
import { customerRegistrationSchema } from '@/schemas/auth.schema';
import { registerCustomer, sendEmailVerification } from '@/authService';
import { useAuth } from '@/components/AuthContext';
import SocialLoginButtons from '@/components/auth/SocialLoginButtons';

export default function RegisterPage() {
  const { t } = useTranslation();
  const { login: _login } = useAuth();

  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    confirmPassword: '',
  });
  const [errors, setErrors] = useState<{ [key: string]: string }>({});
  const [generalError, setGeneralError] = useState('');
  const [registrationSuccess, setRegistrationSuccess] = useState(false);
  const [resendLoading, setResendLoading] = useState(false);
  const [resendMessage, setResendMessage] = useState('');
  const firstNameInputRef = useRef<HTMLInputElement>(null);
  const _router = useRouter();

  useEffect(() => {
    firstNameInputRef.current?.focus();
  }, []);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const fieldName = e.target.id;
    setFormData({ ...formData, [fieldName]: e.target.value });
    // Clear error for this field when user starts typing
    if (errors[fieldName]) {
      setErrors({ ...errors, [fieldName]: '' });
    }
  };

  const getTranslatedError = (message: string): string => {
    if (message.includes('Invalid') || message.includes('email')) {
      return t('validation_invalid_email', 'Invalid email address');
    }
    if (message.includes('at least 6')) {
      return t('validation_min_6_chars', 'Must be at least 6 characters');
    }
    if (message.includes('at least 2')) {
      return t('validation_min_2_chars', 'Must be at least 2 characters');
    }
    if (message.includes('do not match') || message.includes('Passwords')) {
      return t('validation_passwords_match', 'Passwords do not match');
    }
    return message;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrors({});
    setGeneralError('');

    const validationResult = customerRegistrationSchema.safeParse(formData);
    if (!validationResult.success) {
      const fieldErrors: { [key: string]: string } = {};
      validationResult.error.issues.forEach((issue) => {
        const fieldName = issue.path[0] as string;
        fieldErrors[fieldName] = getTranslatedError(issue.message);
      });
      setErrors(fieldErrors);
      return;
    }

    try {
      const response = await registerCustomer(formData);
      if (response?.success) {
        setRegistrationSuccess(true);
      } else {
        const apiErrors = Array.isArray(response?.errors) ? response.errors.join(', ') : '';
        setGeneralError(apiErrors || response?.message || t('failed_to_register', 'Failed to register.'));
      }
    } catch {
      setGeneralError(t('unexpected_error', 'An unexpected error occurred.'));
    }
  };

  const handleResendEmail = async () => {
    setResendLoading(true);
    setResendMessage('');

    try {
      const response = await sendEmailVerification({ email: formData.email });
      if (response?.success || response?.succeeded) {
        setResendMessage(
          t('verification_email_resent', 'Verification email has been resent! Please check your inbox.'),
        );
      } else {
        setResendMessage(t('resend_failed', 'Failed to resend email. Please try again later.'));
      }
    } catch {
      setResendMessage(t('resend_error', 'An error occurred. Please try again.'));
    } finally {
      setResendLoading(false);
      // Clear message after 5 seconds
      setTimeout(() => setResendMessage(''), 5000);
    }
  };

  if (registrationSuccess) {
    return (
      <div className={successStyles.successContainer}>
        <div className={successStyles.successCard}>
          <div className={successStyles.successContent}>
            <div className={successStyles.emailIconWrapper}>
              <Mail size={60} />
              <div className={successStyles.checkmark}>
                <CheckCircle size={24} />
              </div>
            </div>

            <h1 className={successStyles.successTitle}>{t('registration_successful', 'Registration Successful!')}</h1>

            <div className={successStyles.instructionsBox}>
              <p className={successStyles.instructionsText}>
                {t('verify_email_instructions', 'We have sent a verification email to')}
              </p>
              <div className={successStyles.emailHighlight}>{formData.email}</div>

              <ul className={successStyles.stepsList}>
                <li>
                  <span className={successStyles.stepNumber}>1</span>
                  <span>{t('check_inbox', 'Check your inbox (and spam folder)')}</span>
                </li>
                <li>
                  <span className={successStyles.stepNumber}>2</span>
                  <span>{t('click_verification_link', 'Click the verification link in the email')}</span>
                </li>
                <li>
                  <span className={successStyles.stepNumber}>3</span>
                  <span>{t('complete_verification', 'Complete the verification to activate your account')}</span>
                </li>
              </ul>
            </div>

            {resendMessage && (
              <div className={successStyles.resendMessage}>
                <CheckCircle size={18} />
                <span>{resendMessage}</span>
              </div>
            )}

            <div className={successStyles.buttonGroup}>
              <Link href="/auth/login" className={successStyles.loginButton}>
                {t('go_to_login', 'Go to Login')}
                <ArrowRight size={18} />
              </Link>
              <button onClick={handleResendEmail} disabled={resendLoading} className={successStyles.resendButton}>
                <Mail size={18} />
                {resendLoading ? t('sending', 'Sending...') : t('resend_email', 'Resend Email')}
              </button>
            </div>

            <p className={successStyles.helpText}>
              {t('didnt_receive_email', "Didn't receive the email?")}{' '}
              {t('check_spam_or_contact', 'Please check your spam folder.')}
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.authContainer}>
      <form className={styles.authForm} onSubmit={handleSubmit}>
        <h1>{t('register_page_title', 'Register')}</h1>
        {generalError && <p className={styles.errorMessage}>{generalError}</p>}
        <div className={styles.formGroup}>
          <label htmlFor="firstName">{t('first_name', 'First Name')}</label>
          <input
            type="text"
            id="firstName"
            ref={firstNameInputRef}
            value={formData.firstName}
            onChange={handleChange}
            required
            className={errors.firstName ? styles.inputError : ''}
          />
          {errors.firstName && <p className={styles.fieldError}>{errors.firstName}</p>}
        </div>
        <div className={styles.formGroup}>
          <label htmlFor="lastName">{t('last_name', 'Last Name')}</label>
          <input
            type="text"
            id="lastName"
            value={formData.lastName}
            onChange={handleChange}
            required
            className={errors.lastName ? styles.inputError : ''}
          />
          {errors.lastName && <p className={styles.fieldError}>{errors.lastName}</p>}
        </div>
        <div className={styles.formGroup}>
          <label htmlFor="email">{t('email', 'Email')}</label>
          <input
            type="email"
            id="email"
            value={formData.email}
            onChange={handleChange}
            required
            className={errors.email ? styles.inputError : ''}
          />
          {errors.email && <p className={styles.fieldError}>{errors.email}</p>}
        </div>
        <div className={styles.formGroup}>
          <label htmlFor="password">{t('password_label', 'Password')}</label>
          <input
            type="password"
            id="password"
            value={formData.password}
            onChange={handleChange}
            required
            className={errors.password ? styles.inputError : ''}
          />
          {errors.password && <p className={styles.fieldError}>{errors.password}</p>}
        </div>
        <div className={styles.formGroup}>
          <label htmlFor="confirmPassword">{t('confirm_password_label', 'Confirm Password')}</label>
          <input
            type="password"
            id="confirmPassword"
            value={formData.confirmPassword}
            onChange={handleChange}
            required
            className={errors.confirmPassword ? styles.inputError : ''}
          />
          {errors.confirmPassword && <p className={styles.fieldError}>{errors.confirmPassword}</p>}
        </div>
        <button type="submit" className={styles.submitButton}>
          {t('register_button', 'Register')}
        </button>
        <p className={styles.switchFormText}>
          {t('already_have_account', 'Already have an account?')}{' '}
          <Link href="/auth/login">{t('login_button', 'Login')}</Link>
        </p>
        <SocialLoginButtons />
      </form>
    </div>
  );
}
