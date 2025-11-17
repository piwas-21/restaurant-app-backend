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
import { Home, UtensilsCrossed, CalendarCheck, ShoppingCart, LayoutDashboard, Menu, X, ChevronLeft, ChevronRight } from "lucide-react";
import { useCart } from "@/components/cart/CartContext";

const inter = Inter({ subsets: ["latin"] });

export default function AppInternalLayout({ children }: { children: React.ReactNode }) {
  const [isClient, setIsClient] = useState(false);
  const { theme } = useTheme();
  const { user, isLoading } = useAuth();
  const { state: cartState } = useCart();
  const pathname = usePathname();
  const router = useRouter();
  const isHomePage = pathname === '/';
  const isAdminPage = pathname.startsWith('/admin');
  const { t } = useTranslation();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [adminSidebarOpen, setAdminSidebarOpen] = useState(false);
  const headerHeight = "80px";
  const sidebarWidth = "250px";

  // Calculate total cart items
  const cartItemCount = cartState.items.reduce((total, item) => total + item.quantity, 0);

  useEffect(() => {
    setIsClient(true);
    // Language and other effects...
  }, []);

  const useDarkLogoOnHome = isHomePage && theme !== 'dark';
  const logoSrc = (theme === 'dark' || useDarkLogoOnHome)
    ? "/rumi_logo_transparent_dark.png"
    : "/rumi_logo_transparent.png";
  const toggleMobileMenu = () => setMobileMenuOpen(!mobileMenuOpen);
  const closeMobileMenu = () => setMobileMenuOpen(false);
  const toggleAdminSidebar = () => setAdminSidebarOpen(!adminSidebarOpen);
  const closeAdminSidebar = () => setAdminSidebarOpen(false);

  const headerStyles: CSSProperties = {
    padding: "0 1rem",
    color: "var(--text-color)",
    zIndex: 1000,
    height: headerHeight,
    position: isHomePage ? "absolute" : "sticky",
    top: 0,
    left: 0,
    right: 0,
    backgroundColor: isHomePage ? "transparent" : "var(--secondary-color)",
    borderBottom: isHomePage ? "none" : "1px solid var(--border-color)",
    width: "100%",
  };

  const mainStyles: CSSProperties = {
    padding: isHomePage ? "0" : "1rem",
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
          <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`} onClick={closeMobileMenu}>
            <Home size={18} />
            <span>{t('nav_home', 'Home')}</span>
          </Link>
          <Link href="/menu" className={`nav-link ${pathname === '/menu' ? 'active' : ''}`} onClick={closeMobileMenu}>
            <UtensilsCrossed size={18} />
            <span>{t('nav_menu', 'Menu')}</span>
          </Link>
          <Link href="/reservations" className={`nav-link ${pathname === '/reservations' ? 'active' : ''}`} onClick={closeMobileMenu}>
            <CalendarCheck size={18} />
            <span>{t('nav_reservations', 'Reservations')}</span>
          </Link>
          <Link href="/cart" className={`nav-link ${pathname === '/cart' ? 'active' : ''}`} onClick={closeMobileMenu}>
            <ShoppingCart size={18} />
            <span>{t('nav_cart', 'Cart')}</span>
            {cartItemCount > 0 && <span className={navStyles.cartBadge}>{cartItemCount}</span>}
          </Link>
          <Link href="/admin/dashboard" className={`nav-link ${pathname.startsWith('/admin') ? 'active' : ''}`} onClick={closeMobileMenu}>
            <LayoutDashboard size={18} />
            <span>{t('admin_dashboard_title')}</span>
          </Link>
        </>
      );
    }
    return (
      <>
        <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`} onClick={closeMobileMenu}>
          <Home size={18} />
          <span>{t('nav_home', 'Home')}</span>
        </Link>
        <Link href="/menu" className={`nav-link ${pathname === '/menu' ? 'active' : ''}`} onClick={closeMobileMenu}>
          <UtensilsCrossed size={18} />
          <span>{t('nav_menu', 'Menu')}</span>
        </Link>
        <Link href="/reservations" className={`nav-link ${pathname === '/reservations' ? 'active' : ''}`} onClick={closeMobileMenu}>
          <CalendarCheck size={18} />
          <span>{t('nav_reservations', 'Reservations')}</span>
        </Link>
        <Link href="/cart" className={`nav-link ${pathname === '/cart' ? 'active' : ''}`} onClick={closeMobileMenu}>
          <ShoppingCart size={18} />
          <span>{t('nav_cart', 'Cart')}</span>
          {cartItemCount > 0 && <span className={navStyles.cartBadge}>{cartItemCount}</span>}
        </Link>
      </>
    );
  };

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      {isAdminPage && (
        <>
          <Sidebar isOpen={adminSidebarOpen} onClose={closeAdminSidebar} />
          {adminSidebarOpen && (
            <div
              style={{
                position: 'fixed',
                inset: 0,
                backgroundColor: 'rgba(0, 0, 0, 0.5)',
                zIndex: 999,
                display: 'none'
              }}
              className={navStyles.adminSidebarBackdrop}
              onClick={closeAdminSidebar}
              aria-hidden="true"
            />
          )}
        </>
      )}
      <div style={{ flexGrow: 1, display: 'flex', flexDirection: 'column' }} className={isAdminPage ? navStyles.adminMainContent : ''}>
        {isAdminPage && (
          <button
            className={navStyles.adminSidebarToggleFloating}
            onClick={toggleAdminSidebar}
            aria-label={isClient ? (adminSidebarOpen ? t('close_menu') : t('open_menu')) : "Toggle sidebar"}
            aria-expanded={adminSidebarOpen}
          >
            {adminSidebarOpen ? <ChevronLeft size={20} /> : <ChevronRight size={20} />}
          </button>
        )}
        {
          <header style={headerStyles} className={isHomePage && theme !== 'dark' ? 'home-overlay-header' : undefined}>
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
              {!isAdminPage && (
                <button className={navStyles.hamburgerMenu} onClick={toggleMobileMenu} aria-label={isClient ? (mobileMenuOpen ? t('close_menu') : t('open_menu')) : "Open menu"} aria-expanded={mobileMenuOpen}>
                  {mobileMenuOpen ? <X size={28} /> : <Menu size={28} />}
                </button>
              )}
              {/* Mobile menu backdrop */}
              <div
                className={`${navStyles.mobileMenuBackdrop} ${mobileMenuOpen ? navStyles.visible : ''}`}
                onClick={closeMobileMenu}
                aria-hidden="true"
              />
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
        }
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
