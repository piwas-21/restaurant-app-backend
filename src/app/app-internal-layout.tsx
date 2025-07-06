"use client";

import { Inter } from "next/font/google";
import { useTranslation } from "react-i18next";
import i18n from "../i18n";
import LanguageSwitcher from "@/components/LanguageSwitcher";
import { useEffect, useState, CSSProperties } from "react";
import Link from "next/link";
import Image from "next/image";
import { useTheme } from "@/components/ThemeContext";
import ThemeSwitcher from "@/components/ThemeSwitcher";
import { usePathname, useRouter } from "next/navigation";
import navStyles from "./styles/Header.module.css";
import CookieConsentBanner from "@/components/CookieConsentBanner";
import CookieSettingsModal from "@/components/CookieSettingsModal";
import FooterCookieLink from "@/components/FooterCookieLink";
import UserMenu from "@/components/UserMenu";
import { useAuth } from "@/components/AuthContext";

const inter = Inter({ subsets: ["latin"] });

export default function AppInternalLayout({ children }: { children: React.ReactNode }) {
  const [isClient, setIsClient] = useState(false);
  const { theme } = useTheme();
  const { user, isLoading } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const isHomePage = pathname === '/';
  const { t } = useTranslation();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const headerHeight = "80px";

  useEffect(() => {
    setIsClient(true);
    const preferredLang = i18n.language || (typeof window !== "undefined" ? window.localStorage.getItem("i18nextLng") : null) || "en";
    if (document.documentElement.lang !== preferredLang.split("-")[0]) {
      document.documentElement.lang = preferredLang.split("-")[0];
    }

    const handleLanguageChanged = (lng: string) => {
      if (document.documentElement.lang !== lng.split("-")[0]) {
        document.documentElement.lang = lng.split("-")[0];
      }
    };
    i18n.on("languageChanged", handleLanguageChanged);

    return () => {
      i18n.off("languageChanged", handleLanguageChanged);
    };
  }, [i18n]);

  useEffect(() => {
    if (isLoading) return;

    const userRole = user?.role.toLowerCase();
    const protectedRoutes: { [key:string]: string[] } = {
      '/admin': ['admin'],
      '/cashier': ['admin', 'cashier'],
      '/kitchen-staff': ['admin', 'kitchen-staff'],
      '/server': ['admin', 'server'],
      '/account': ['customer']
    };

    const currentRoute = Object.keys(protectedRoutes).find(route => pathname.startsWith(route));

    if (currentRoute) {
      if (!user || !protectedRoutes[currentRoute].includes(userRole as string)) {
        router.replace('/auth/login');
        return;
      }
    }

    const staffRedirects: { [key: string]: string } = {
      'cashier': '/cashier',
      'kitchen-staff': '/kitchen-staff',
      'server': '/server',
    };

    const requiredPath = userRole ? staffRedirects[userRole] : undefined;

    if (requiredPath && pathname !== requiredPath) {
      router.replace(requiredPath);
    }
  }, [user, isLoading, pathname, router]);

  useEffect(() => {
    if (isClient) {
      document.documentElement.setAttribute("data-theme", theme);
      if (!document.body.classList.contains(inter.className)) {
        document.body.className = `${document.body.className} ${inter.className}`.trim();
      }
    }
  }, [theme, isClient]);

  useEffect(() => {
    if (isClient) {
      if (mobileMenuOpen) {
        document.body.classList.add("mobile-menu-open");
      } else {
        document.body.classList.remove("mobile-menu-open");
      }
    }
    return () => {
      if (isClient) {
        document.body.classList.remove("mobile-menu-open");
      }
    };
  }, [mobileMenuOpen, isClient]);

  const logoSrc = theme === 'dark' ? "/rumi_logo_transparent_dark.png" : "/rumi_logo_transparent.png";
  const toggleMobileMenu = () => setMobileMenuOpen(!mobileMenuOpen);
  const closeMobileMenu = () => setMobileMenuOpen(false);

  const commonHeaderProperties: CSSProperties = {
    padding: "0 1rem",
    color: "var(--text-color)",
    zIndex: 1000,
    height: headerHeight,
    width: "100%",
    left: 0,
    right: 0,
  };

  const headerSpecificStyles: CSSProperties = isHomePage ? {
    ...commonHeaderProperties,
    backgroundColor: "transparent",
    borderBottom: "none",
    position: "absolute",
    top: 0,
  } : {
    ...commonHeaderProperties,
    backgroundColor: "var(--secondary-color)",
    borderBottom: "1px solid var(--border-color)",
    position: "sticky",
    top: 0,
  };

  const renderNavLinks = () => {
    if (isLoading) return null;

    const role = user?.role.toLowerCase();

    if (role === 'admin') {
      return (
        <>
          <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_home', 'Home')}</Link>
          <Link href="/menu" className={`nav-link ${pathname === '/menu' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_menu', 'Menu')}</Link>
          <Link href="/reservations" className={`nav-link ${pathname === '/reservations' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_reservations', 'Reservations')}</Link>
          <Link href="/cart" className={`nav-link ${pathname === '/cart' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_cart', 'Cart')}</Link>
          <Link href="/admin/dashboard" className={`nav-link ${pathname.startsWith('/admin') ? 'active' : ''}`} onClick={closeMobileMenu}>{t('admin_dashboard_title')}</Link>
          <Link href="/cashier" className={`nav-link ${pathname === '/cashier' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_cashier')}</Link>
          <Link href="/kitchen-staff" className={`nav-link ${pathname === '/kitchen-staff' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_kitchen')}</Link>
          <Link href="/server" className={`nav-link ${pathname === '/server' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_server')}</Link>
        </>
      );
    }

    if (role === 'cashier' || role === 'kitchen-staff' || role === 'server') {
      return null;
    }

    return (
      <>
        <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_home', 'Home')}</Link>
        <Link href="/menu" className={`nav-link ${pathname === '/menu' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_menu', 'Menu')}</Link>
        <Link href="/reservations" className={`nav-link ${pathname === '/reservations' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_reservations', 'Reservations')}</Link>
        <Link href="/cart" className={`nav-link ${pathname === '/cart' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_cart', 'Cart')}</Link>
      </>
    );
  };

  return (
    <>
      <header style={headerSpecificStyles}>
        <div style={{
          maxWidth: "1200px",
          margin: "0 auto",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          height: "100%"
        }}>
          <Link href="/" style={{ textDecoration: "none", color: "var(--primary-color)", display: "flex", alignItems: "center" }} onClick={closeMobileMenu}>
            <Image src={logoSrc} alt="RUMI Restaurant Logo" width={180} height={90} style={{ marginRight: "10px", objectFit: "contain", maxHeight: `calc(${headerHeight} - 10px)` }} priority />
          </Link>
          <button className={navStyles.hamburgerMenu} onClick={toggleMobileMenu} aria-label={isClient ? (mobileMenuOpen ? t('close_menu') : t('open_menu')) : "Open menu"} aria-expanded={mobileMenuOpen}>
            {mobileMenuOpen ?
              <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" fill="currentColor" viewBox="0 0 16 16">
                <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8 2.146 2.854Z"/>
              </svg>
              :
              <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" fill="currentColor" viewBox="0 0 16 16">
                <path fillRule="evenodd" d="M2.5 12a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5z"/>
              </svg>
            }
          </button>
          <nav className={`${navStyles.navLinksContainer} ${mobileMenuOpen ? navStyles.mobileMenuOpen : ''}`}>
            {renderNavLinks()}
            {isClient && !isLoading && (
              <>
                {user ? <UserMenu /> : <Link href="/auth/login" className={`nav-link ${pathname === '/auth/login' ? 'active' : ''}`} onClick={closeMobileMenu}>{t('nav_login', 'Login')}</Link>}
                <div className={navStyles.switcherGroup}>
                  <LanguageSwitcher />
                  <ThemeSwitcher />
                </div>
              </>
            )}
          </nav>
        </div>
      </header>
      <main style={{
          paddingLeft: isHomePage ? "0" : "1rem",
          paddingRight: isHomePage ? "0" : "1rem",
          paddingBottom: isHomePage ? "0" : "1rem",
          maxWidth: isHomePage ? "none" : "1200px",
          margin: isHomePage ? "0" : "0 auto",
          paddingTop: isHomePage ? "0" : headerHeight,
          minHeight: `calc(100vh - ${headerHeight} - ${!isHomePage ? '150px' : '0px'})`
      }}>
          {children}
      </main>
      {!isHomePage && (
        <footer style={{
          padding: "2rem 1rem",
          backgroundColor: "var(--secondary-color)",
          color: "var(--text-color)",
          textAlign: "center",
          marginTop: "auto",
          borderTop: "1px solid var(--border-color)"
        }}>
          <p>&copy; {new Date().getFullYear()} RUMI Restaurant. All rights reserved.</p>
          <p>{isClient ? t("rumi_address_street") : "Rue du Grand-Pré 45"}, {isClient ? t("rumi_address_city_country") : "1202 Genève, Switzerland"}</p>
          <FooterCookieLink />
        </footer>
      )}
      <CookieConsentBanner />
      <CookieSettingsModal />
    </>
  );
}
