'use client';

import { ThemeProvider } from "@/components/ThemeContext";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n";
import { CookieConsentProvider } from "@/components/CookieConsentContext";
import { CartProvider } from "@/components/cart/CartContext";
import { SnackbarProvider, closeSnackbar } from "notistack";
import React from "react";
import { AuthProvider } from "@/components/AuthContext";
import { SessionProvider } from "@/contexts/SessionContext";
import { CheckoutProvider } from "@/contexts/CheckoutContext";
import { X } from "lucide-react";

export default function ClientProviders({ children }: { children: React.ReactNode }) {
  return (
    <AuthProvider>
      <SessionProvider>
        <ThemeProvider>
          <I18nextProvider i18n={i18n}>
            <CookieConsentProvider>
              <CartProvider>
                <CheckoutProvider>
                  <SnackbarProvider
                    maxSnack={3}
                    anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                    autoHideDuration={4000}
                    action={(snackbarKey) => (
                      <button
                        onClick={() => closeSnackbar(snackbarKey)}
                        style={{
                          background: 'none',
                          border: 'none',
                          color: 'inherit',
                          cursor: 'pointer',
                          padding: '4px',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          marginLeft: '8px',
                          opacity: 0.8,
                          transition: 'opacity 0.2s',
                        }}
                        onMouseEnter={(e) => e.currentTarget.style.opacity = '1'}
                        onMouseLeave={(e) => e.currentTarget.style.opacity = '0.8'}
                        aria-label="Close notification"
                      >
                        <X size={18} strokeWidth={2} />
                      </button>
                    )}
                  >
                    {children}
                  </SnackbarProvider>
                </CheckoutProvider>
              </CartProvider>
            </CookieConsentProvider>
          </I18nextProvider>
        </ThemeProvider>
      </SessionProvider>
    </AuthProvider>
  );
}
