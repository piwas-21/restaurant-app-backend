'use client';

import { ThemeProvider } from "@/components/ThemeContext";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n";
import { CookieConsentProvider } from "@/components/CookieConsentContext";
import { CartProvider } from "@/components/cart/CartContext";
import { SnackbarProvider } from "notistack";
import React from "react";
import { AuthProvider } from "@/components/AuthContext";

export default function ClientProviders({ children }: { children: React.ReactNode }) {
  return (
    <AuthProvider>
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <CookieConsentProvider>
            <CartProvider>
              <SnackbarProvider maxSnack={3} anchorOrigin={{ vertical: "top", horizontal: "center" }}>
                {children}
              </SnackbarProvider>
            </CartProvider>
          </CookieConsentProvider>
        </I18nextProvider>
      </ThemeProvider>
    </AuthProvider>
  );
}
