// src/app/layout.tsx
"use client"; 

import { Inter } from "next/font/google";
import "./globals.css";
import { CartProvider } from "@/components/cart/CartContext";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n"; 
import LanguageSwitcher from "@/components/LanguageSwitcher"; 
import { useEffect, useState } from "react";
import Link from "next/link";
import Image from "next/image"; // Import Next.js Image component
import { ThemeProvider, useTheme } from "@/components/ThemeContext"; 
import ThemeSwitcher from "@/components/ThemeSwitcher";
import { SnackbarProvider } from "notistack"; // Import notistack

const inter = Inter({ subsets: ["latin"] });

function AppLayout({ children }: { children: React.ReactNode }) {
  const [isClient, setIsClient] = useState(false);
  const { theme } = useTheme(); 

  useEffect(() => {
    setIsClient(true);
    const currentLang = i18n.language || window.localStorage.getItem("i18nextLng") || "en";
    document.documentElement.lang = currentLang.split("-")[0];

    const handleLanguageChanged = (lng: string) => {
      document.documentElement.lang = lng.split("-")[0];
    };
    i18n.on("languageChanged", handleLanguageChanged);

    return () => {
      i18n.off("languageChanged", handleLanguageChanged);
    };
  }, []);

  useEffect(() => {
    if (isClient) {
        document.documentElement.setAttribute("data-theme", theme);
    }
  }, [theme, isClient]);

  const langForHtml = isClient ? (i18n.language.split("-")[0] || "en") : "en";
  const themeForHtml = isClient ? theme : "light"; 

  return (
    <html lang={langForHtml} data-theme={themeForHtml} suppressHydrationWarning={true}>
      <body className={inter.className}>
        <I18nextProvider i18n={i18n}>
          <CartProvider>
            <SnackbarProvider maxSnack={3} anchorOrigin={{ vertical: "top", horizontal: "center"}}>
              <header style={{ padding: "0.5rem 1rem", backgroundColor: "var(--secondary-color)", color: "var(--text-color)", textAlign: "center", borderBottom: "1px solid var(--border-color)" }}>
                <div style={{ maxWidth: "1200px", margin: "0 auto", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <Link href="/" style={{ textDecoration: "none", color: "var(--primary-color)", display: "flex", alignItems: "center" }}>
                    <Image src="/rumi_logo.png" alt="RUMI Restaurant Logo" width={50} height={50} style={{ marginRight: "10px" }} />
                    <span style={{ fontSize: "1.5rem", fontWeight: "bold" }}>RUMI Restaurant</span>
                  </Link>
                  <nav style={{display: "flex", gap: "1rem", alignItems: "center"}}>
                    <Link href="/" style={{color: "var(--text-color)"}}>Home</Link>
                    <Link href="/menu" style={{color: "var(--text-color)"}}>Menu</Link>
                    <Link href="/reservations" style={{color: "var(--text-color)"}}>Reservations</Link>
                    <Link href="/cart" style={{color: "var(--text-color)"}}>Cart</Link> 
                    {isClient && <LanguageSwitcher />}
                    {isClient && <ThemeSwitcher />}
                  </nav>
                </div>
              </header>
              <main style={{padding: "1rem", maxWidth: "1200px", margin: "0 auto"}}>
                  {children}
              </main>
              <footer style={{ padding: "2rem 1rem", backgroundColor: "var(--secondary-color)", color: "var(--text-color)", textAlign: "center", marginTop: "2rem", borderTop: "1px solid var(--border-color)" }}>
                <p>&copy; {new Date().getFullYear()} RUMI Restaurant. All rights reserved.</p>
                <p>Rue de Berne 13, 1201 Genève, Switzerland</p>
              </footer>
            </SnackbarProvider>
          </CartProvider>
        </I18nextProvider>
      </body>
    </html>
  );
}

export default function RootLayoutOuter({ children }: { children: React.ReactNode }) {
    return (
        <ThemeProvider>
            <AppLayout>{children}</AppLayout>
        </ThemeProvider>
    );
}

