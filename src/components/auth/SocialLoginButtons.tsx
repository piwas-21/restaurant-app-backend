"use client";

import React from 'react';
import { GoogleLogin, CredentialResponse } from '@react-oauth/google';
import AppleLogin from 'react-apple-login';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import { useTheme } from '@/components/ThemeContext';
import { useAuth } from '@/components/AuthContext';
import { googleLogin, appleLogin } from '@/services/authService';
import toast from 'react-hot-toast';
import styles from './SocialLoginButtons.module.css';

export default function SocialLoginButtons() {
  const { t } = useTranslation();
  const router = useRouter();
  const { login } = useAuth();
  const { theme } = useTheme();

  const handleGoogleSuccess = async (credentialResponse: CredentialResponse) => {
    if (credentialResponse.credential) {
      try {
        const response = await googleLogin(credentialResponse.credential);
        if (response.success) {
          login(response.data);
          toast.success(t('login_success', 'Logged in successfully!'));
          
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
          toast.error(response.message || t('login_failed', 'Login failed'));
        }
      } catch (error) {
        console.error("Google login error:", error);
        toast.error(t('login_error', 'An error occurred during login'));
      }
    }
  };

  const handleGoogleError = () => {
    toast.error(t('google_login_failed', 'Google Login Failed'));
  };

  //   const handleAppleSuccess = async (response: any) => {
  //     if (!response.error) {
  //         try {
  //             // response.authorization.id_token
  //             // response.user (only on first login)
  //             const authResponse = await appleLogin(
  //                 response.authorization.id_token,
  //                 response.user ? {
  //                     firstName: response.user.name.firstName,
  //                     lastName: response.user.name.lastName
  //                 } : undefined
  //             );

  //             if (authResponse.success) {
  //                 login(authResponse.data);
  //                 toast.success(t('login_success', 'Logged in successfully!'));
  //                 // Redirect logic same as above
  //                  const userRole = authResponse.data.role.toLowerCase();
  //                 switch (userRole) {
  //                     case "admin":
  //                     router.push("/admin/dashboard");
  //                     break;
  //                     case "customer":
  //                     router.push("/account");
  //                     break;
  //                     case "cashier":
  //                     router.push("/cashier");
  //                     break;
  //                     case "kitchen-staff":
  //                     router.push("/kitchen-staff");
  //                     break;
  //                     case "server":
  //                     router.push("/server");
  //                     break;
  //                     default:
  //                     router.push("/");
  //                     break;
  //                 }
  //             } else {
  //                 toast.error(authResponse.message || t('login_failed', 'Login failed'));
  //             }
  //         } catch (error) {
  //             console.error("Apple login error:", error);
  //             toast.error(t('login_error', 'An error occurred during login'));
  //         }
  //     } else {
  //         toast.error(t('apple_login_failed', 'Apple Login Failed'));
  //     }
  //   };

  //   const appleClientId = process.env.NEXT_PUBLIC_APPLE_CLIENT_ID || "com.example.service";
  //   const appleRedirectURI = process.env.NEXT_PUBLIC_APPLE_REDIRECT_URI || "https://example.com/auth/apple/callback";

  return (
    <div className={styles.container}>
      <div className={styles.divider}>
        <span>{t('or_continue_with', 'Or continue with')}</span>
      </div>
      <div className={styles.buttons}>
        <div className={styles.googleWrapper}>
            <GoogleLogin
            onSuccess={handleGoogleSuccess}
            onError={handleGoogleError}
            useOneTap
            theme={theme === 'light' ? 'outline' : 'filled_black'}
            shape="rectangular"
            width="100%"
            />
        </div>
        
        {/* Apple Login requires HTTPS and valid configuration to work properly */}
        {/* <div className={styles.appleWrapper}>
            <AppleLogin
            clientId={appleClientId}
            redirectURI={appleRedirectURI}
            usePopup={true}
            callback={handleAppleSuccess}
            scope="email name"
            responseMode="query"
            render={(renderProps) => (
                <button 
                onClick={renderProps.onClick} 
                className={styles.appleButton}
                disabled={renderProps.disabled}
                >
                 <svg className={styles.appleIcon} viewBox="0 0 384 512" width="18" height="18">
                    <path fill="currentColor" d="M318.7 268.7c-.2-36.7 16.4-64.4 50-84.8-18.8-26.9-47.2-41.7-84.7-44.6-35.5-2.8-74.3 20.7-88.5 20.7-15 0-49.4-19.7-76.4-19.7C63.3 141.2 4 184.8 4 273.5q0 39.3 14.4 81.2c12.8 36.7 59 126.7 107.2 125.2 25.2-.6 43-17.9 75.8-17.9 31.8 0 48.3 17.9 76.4 17.9 48.6-.7 90.4-82.5 102.6-119.3-65.2-30.7-61.7-90-61.7-91.9zm-56.6-164.2c27.3-32.4 24.8-61.9 24-72.5-24.1 1.4-52 16.4-67.9 34.9-17.5 19.8-27.8 44.3-25.6 71.9 26.1 2 52.3-11.4 69.5-34.3z"/>
                 </svg>
                 Sign in with Apple
                </button>
            )}
            />
        </div> */}
      </div>
    </div>
  );
}
