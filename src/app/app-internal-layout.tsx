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
import Sidebar from "@/components/admin/Sidebar";

const inter = Inter({ subsets: ["latin"] });

export default function AppInternalLayout({ children }: { children: React.ReactNode }) {
  const [isClient, setIsClient] = useState(false);
  const { theme } = useTheme();
  const { user, isLoading } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const isHomePage = pathname === '/';
  const isAdminPage = pathname.startsWith('/admin');
  const { t } = useTranslation();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const headerHeight = "80px";
  const sidebarWidth = "250px";

  useEffect(() => {
    setIsClient(true);
    // Language and other effects...
  }, []);

  const logoSrc = theme === 'dark' ? "/rumi_logo_transparent_dark.png" : "/rumi_logo_transparent.png";
  const toggleMobileMenu = () => setMobileMenuOpen(!mobileMenuOpen);
  const closeMobileMenu = () => setMobileMenuOpen(false);

  const headerStyles: CSSProperties = {
    padding: "0 1rem",
    color: "var(--text-color)",
    zIndex: 1000,
    height: headerHeight,
    position: "sticky",
    top: 0,
    backgroundColor: "var(--secondary-color)",
    borderBottom: "1px solid var(--border-color)",
    width: "100%",
  };

  const mainStyles: CSSProperties = {
    padding: "1rem",
    flexGrow: 1,
  };

  const footerStyles: CSSProperties = {
    padding: "2rem 1rem",
    backgroundColor: "var(--secondary-color)",
    color: "var(--text-color)",
    textAlign: "center",
    borderTop: "1px solid var(--border-color)",
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
        </>
      );
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
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      {isAdminPage && <Sidebar />}
      <div style={{ flexGrow: 1, display: 'flex', flexDirection: 'column', marginLeft: isAdminPage ? sidebarWidth : "0" }}>
        {!isHomePage && (
          <header style={headerStyles}>
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
                {/* SVG icons */}
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
        )}
        <main style={mainStyles}>
            {children}
        </main>
        {!isHomePage && (
          <footer style={footerStyles}>
            <p>&copy; {new Date().getFullYear()} RUMI Restaurant. All rights reserved.</p>
            <p>{isClient ? t("rumi_address_street") : "Rue du Grand-Pré 45"}, {isClient ? t("rumi_address_city_country") : "1202 Genève, Switzerland"}</p>
            <FooterCookieLink />
          </footer>
        )}
      </div>
      <CookieConsentBanner />
      <CookieSettingsModal />
    </div>
  );
}
