'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/components/AuthContext';

interface AdminAuthGuardProps {
  children: React.ReactNode;
  requiredRoles?: string[];
}

/**
 * Authentication guard for admin pages
 * Redirects to home page if user is not authenticated or doesn't have required role
 */
export const AdminAuthGuard: React.FC<AdminAuthGuardProps> = ({
  children,
  requiredRoles = ['Admin', 'Staff']
}) => {
  const { user, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (isLoading) return;

    // Redirect if not authenticated
    if (!user) {
      router.push('/');
      return;
    }

    // Redirect if user doesn't have required role
    if (!requiredRoles.includes(user.role)) {
      router.push('/');
      return;
    }
  }, [user, isLoading, router, requiredRoles]);

  // Show loading state while checking authentication
  if (isLoading) {
    return (
      <div style={{
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        minHeight: '100vh',
        fontSize: '1.125rem',
        color: 'var(--text-secondary)'
      }}>
        Loading...
      </div>
    );
  }

  // Show nothing if not authenticated (redirect is in progress)
  if (!user || !requiredRoles.includes(user.role)) {
    return null;
  }

  // Render children if authenticated and authorized
  return <>{children}</>;
};
