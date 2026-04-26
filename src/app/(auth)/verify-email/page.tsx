'use client';
import { useEffect, useState, Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslation } from 'react-i18next';
import { CheckCircle, XCircle, ArrowRight, Loader2 } from 'lucide-react';
import { verifyEmail } from '@/authService';
import styles from './VerifyEmail.module.css';

export default function VerifyEmailPage() {
  const { t } = useTranslation();

  return (
    <Suspense fallback={
      <div className={styles.container}>
        <div className={styles.card}>
          <div className={styles.content}>
            <div className={styles.loadingContainer}>
              <div className={styles.spinner}></div>
              <p className={styles.loadingText}>{t('loading', 'Loading...')}</p>
            </div>
          </div>
        </div>
      </div>
    }>
      <VerifyEmailContent />
    </Suspense>
  );
}

function VerifyEmailContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const { t } = useTranslation();

  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState(t('verifying_email', 'Verifying your email...'));

  useEffect(() => {
    const verifyUserEmail = async () => {
      const email = searchParams.get('email');
      const token = searchParams.get('token');

      if (!email || !token) {
        setStatus('error');
        setMessage(t('invalid_verification_link', 'Invalid verification link. Missing email or token.'));
        return;
      }

      try {
        const response = await verifyEmail({ email, token });

        // Debug log
        console.log('Email verification response:', response);

        if (response.succeeded || response.success) {
          setStatus('success');
          setMessage(response.data || response.message || t('email_verified_success', 'Your email has been successfully verified! You can now access all features.'));
          // Redirect to login after 3 seconds
          setTimeout(() => {
            router.push('/auth/login?verified=true');
          }, 3000);
        } else {
          setStatus('error');
          const errorMsg = response.messages?.[0] || response.message || t('email_verification_failed', 'Failed to verify email. The link may have expired or is invalid.');
          setMessage(errorMsg);
        }
      } catch (error) {
        console.error('Email verification error:', error);
        setStatus('error');
        setMessage(t('unexpected_error', 'An unexpected error occurred. Please try again later.'));
      }
    };

    verifyUserEmail();
  }, [searchParams, router, t]);

  return (
    <div className={styles.container}>
      <div className={styles.card}>
        <div className={styles.content}>
          {status === 'loading' && (
            <div className={styles.loadingContainer}>
              <div className={styles.spinner}></div>
              <h1 className={styles.loadingTitle}>{t('verifying_title', 'Verifying Email')}</h1>
              <p className={styles.loadingText}>{message}</p>
            </div>
          )}

          {status === 'success' && (
            <div className={styles.successContainer}>
              <div className={styles.successIconWrapper}>
                <CheckCircle size={50} />
              </div>
              <h1 className={styles.successTitle}>{t('email_verified_title', 'Email Verified!')}</h1>
              <p className={styles.successMessage}>{message}</p>
              <p className={styles.redirectText}>
                <Loader2 size={16} className="animate-spin" />
                {t('redirecting_login', 'Redirecting to login')}
                <span className="dots">
                  <span></span>
                  <span></span>
                  <span></span>
                </span>
              </p>
              <Link href="/auth/login" className={styles.loginButton}>
                {t('go_to_login', 'Go to Login')}
                <ArrowRight size={18} />
              </Link>
            </div>
          )}

          {status === 'error' && (
            <div className={styles.errorContainer}>
              <div className={styles.errorIconWrapper}>
                <XCircle size={50} />
              </div>
              <h1 className={styles.errorTitle}>{t('verification_failed_title', 'Verification Failed')}</h1>
              <p className={styles.errorMessage}>{message}</p>
              <Link href="/auth/login" className={styles.backButton}>
                <ArrowRight size={18} style={{ transform: 'rotate(180deg)' }} />
                {t('back_to_login', 'Back to Login')}
              </Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
