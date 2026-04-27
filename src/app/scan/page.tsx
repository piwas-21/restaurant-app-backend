'use client';

import { Suspense, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useTableContext } from '@/contexts/TableContext';

interface TableValidation {
  isValid: boolean;
  tableId: string;
  tableNumber: string;
  maxGuests: number;
  isOutdoor: boolean;
  qrCodeGeneratedAt?: string;
}

function ScanPageContent() {
  const { t } = useTranslation();
  const router = useRouter();
  const searchParams = useSearchParams();
  const { setTableContext } = useTableContext();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const validateQRCode = async () => {
      const qrCode = searchParams.get('qr');

      if (!qrCode) {
        setError(t('qr_code_missing'));
        setLoading(false);
        return;
      }

      try {
        const response = await fetch(
          `${process.env.NEXT_PUBLIC_API_URL}/api/Tables/validate-qr/${encodeURIComponent(qrCode)}`,
        );

        const result = await response.json();

        if (!response.ok || !result.success) {
          setError(result.message || t('qr_code_invalid'));
          setLoading(false);
          return;
        }

        const tableData: TableValidation = result.data;

        // Store table information using TableContext
        setTableContext({
          tableId: tableData.tableId,
          tableNumber: tableData.tableNumber,
          qrScanned: true,
          isOutdoor: tableData.isOutdoor,
        });

        // Redirect to menu with table context
        setTimeout(() => {
          router.push('/menu');
        }, 1000);
      } catch (err) {
        // Log error for debugging
        if (process.env.NODE_ENV === 'development') {
          console.error('Error validating QR code:', err);
        }
        setError(t('qr_code_validation_error'));
        setLoading(false);
      }
    };

    validateQRCode();
  }, [searchParams, router, t, setTableContext]);

  if (loading) {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '100vh',
          padding: '2rem',
          textAlign: 'center',
        }}
      >
        <div
          style={{
            width: '50px',
            height: '50px',
            border: '3px solid var(--color-border)',
            borderTop: '3px solid var(--color-primary)',
            borderRadius: '50%',
            animation: 'spin 1s linear infinite',
          }}
        />
        <h2 style={{ marginTop: '1.5rem', color: 'var(--color-text)' }}>{t('validating_qr_code')}</h2>
        <p style={{ marginTop: '0.5rem', color: 'var(--color-text-muted)' }}>{t('please_wait')}</p>
      </div>
    );
  }

  if (error) {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '100vh',
          padding: '2rem',
          textAlign: 'center',
        }}
      >
        <div
          style={{
            fontSize: '3rem',
            marginBottom: '1rem',
          }}
        >
          ❌
        </div>
        <h2 style={{ color: 'var(--color-error)', marginBottom: '0.5rem' }}>{t('qr_code_error')}</h2>
        <p style={{ color: 'var(--color-text-muted)', marginBottom: '2rem' }}>{error}</p>
        <button
          onClick={() => router.push('/menu')}
          style={{
            padding: '0.75rem 2rem',
            backgroundColor: 'var(--color-primary)',
            color: 'white',
            border: 'none',
            borderRadius: '8px',
            fontSize: '1rem',
            cursor: 'pointer',
            transition: 'opacity 0.2s',
          }}
          onMouseEnter={(e) => (e.currentTarget.style.opacity = '0.9')}
          onMouseLeave={(e) => (e.currentTarget.style.opacity = '1')}
        >
          {t('go_to_menu')}
        </button>
      </div>
    );
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '100vh',
        padding: '2rem',
        textAlign: 'center',
      }}
    >
      <div
        style={{
          fontSize: '3rem',
          marginBottom: '1rem',
        }}
      >
        ✅
      </div>
      <h2 style={{ color: 'var(--color-success)', marginBottom: '0.5rem' }}>{t('qr_code_valid')}</h2>
      <p style={{ color: 'var(--color-text-muted)' }}>{t('redirecting_to_menu')}</p>
    </div>
  );
}

export default function ScanPage() {
  return (
    <Suspense
      fallback={
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: '100vh',
            padding: '2rem',
            textAlign: 'center',
          }}
        >
          <div
            style={{
              width: '50px',
              height: '50px',
              border: '3px solid var(--color-border)',
              borderTop: '3px solid var(--color-primary)',
              borderRadius: '50%',
              animation: 'spin 1s linear infinite',
            }}
          />
          <h2 style={{ marginTop: '1.5rem', color: 'var(--color-text)' }}>Loading...</h2>
        </div>
      }
    >
      <ScanPageContent />
    </Suspense>
  );
}
