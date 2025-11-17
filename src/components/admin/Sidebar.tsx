'use client';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { Users, FolderTree, UtensilsCrossed, Sparkles, Award, Gift, TrendingUp, ClipboardList, CalendarCheck, MapPin, BarChart3, DollarSign } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';

interface SidebarProps {
  isOpen?: boolean;
  onClose?: () => void;
}

const Sidebar = ({ isOpen = true, onClose }: SidebarProps) => {
  const { t, i18n } = useTranslation();
  const pathname = usePathname();
  const [isClient, setIsClient] = useState(false);

  // Ensure we're on client side and language is loaded
  useEffect(() => {
    setIsClient(true);
  }, []);

  // Force re-render when language changes
  useEffect(() => {
    const handleLanguageChange = () => {
      setIsClient(true);
    };

    i18n.on('languageChanged', handleLanguageChange);

    return () => {
      i18n.off('languageChanged', handleLanguageChange);
    };
  }, [i18n]);

  const navItems = [
    {
      href: '/admin/member-management',
      key: 'admin_member_management_title',
      icon: Users
    },
    {
      href: '/admin/category-management',
      key: 'admin_category_management_title',
      icon: FolderTree
    },
    {
      href: '/admin/menu-management',
      key: 'admin_menu_management_title',
      icon: UtensilsCrossed
    },
    {
      href: '/admin/specials-management',
      key: 'admin_specials_management_title',
      icon: Sparkles
    },
    {
      href: '/admin/orders-management',
      key: 'admin_orders_management',
      fallback: 'Orders Management',
      icon: ClipboardList
    },
    {
      href: '/admin/reservations-management',
      key: 'admin_reservations_management',
      fallback: 'Reservations Management',
      icon: CalendarCheck
    },
    {
      href: '/admin/table-layout-editor',
      key: 'table_layout_editor',
      fallback: 'Table Layout Editor',
      icon: MapPin
    },
    {
      href: '/admin/table-statistics',
      key: 'table_statistics',
      fallback: 'Table Statistics',
      icon: BarChart3
    },
    {
      href: '/admin/point-rules',
      key: 'point_rules',
      fallback: 'Point Rules',
      icon: Award
    },
    {
      href: '/admin/customer-discounts',
      key: 'customer_discounts',
      fallback: 'Customer Discounts',
      icon: Gift
    },
    {
      href: '/admin/tax-configuration',
      key: 'tax_configuration',
      fallback: 'Tax Configuration',
      icon: DollarSign
    },
    {
      href: '/admin/fidelity-analytics',
      key: 'fidelity_analytics',
      fallback: 'Fidelity Analytics',
      icon: TrendingUp
    },
  ];

  return (
    <aside className={`${styles.sidebar} ${isOpen ? styles.sidebarOpen : ''}`}>
      <div className={styles.sidebarTitle} suppressHydrationWarning>
        {isClient ? t('admin_dashboard_title') : 'Admin Dashboard'}
      </div>
      <hr className={styles.sidebarDivider} />
      <nav>
        <ul>
          {navItems.map((item) => {
            const Icon = item.icon;
            const label = isClient
              ? t(item.key, item.fallback || item.key)
              : (item.fallback || item.key);

            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className={pathname.startsWith(item.href) ? styles.activeLink : ''}
                  onClick={onClose}
                >
                  <Icon size={20} strokeWidth={2} />
                  <span suppressHydrationWarning>{label}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
    </aside>
  );
};

export default Sidebar;
