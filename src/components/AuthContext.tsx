'use client';

import React, { createContext, useState, useEffect, useContext, ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { refreshToken } from '@/services/authService';

interface User {
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  accessToken: string;
}

interface AuthContextType {
  user: User | null;
  login: (userData: User) => void;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const router = useRouter();

  useEffect(() => {
    const validateSession = async () => {
      try {
        const storedUser = localStorage.getItem('user');
        const authToken = localStorage.getItem('auth_token');
        const refreshTokenValue = localStorage.getItem('refresh_token');

        if (storedUser && authToken && refreshTokenValue) {
          // Validate the token by attempting a refresh
          try {
            const refreshResponse = await refreshToken();

            if (refreshResponse.success) {
              // Token is valid or successfully refreshed
              setUser(JSON.parse(storedUser));
            } else {
              // Token refresh failed - clear auth state
              localStorage.removeItem('auth_token');
              localStorage.removeItem('refresh_token');
              localStorage.removeItem('user');
              setUser(null);
            }
          } catch {
            // Token validation/refresh failed - clear auth state
            localStorage.removeItem('auth_token');
            localStorage.removeItem('refresh_token');
            localStorage.removeItem('user');
            setUser(null);
          }
        }
      } catch (error) {
        console.error('Failed to validate session', error);
      } finally {
        setIsLoading(false);
      }
    };

    validateSession();
  }, []);

  const login = (userData: User) => {
    localStorage.setItem('user', JSON.stringify(userData));
    setUser(userData);
  };

  const logout = () => {
    localStorage.removeItem('user');
    setUser(null);
    router.push('/');
  };

  return <AuthContext.Provider value={{ user, login, logout, isLoading }}>{children}</AuthContext.Provider>;
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
