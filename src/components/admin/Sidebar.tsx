'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { Users, FolderTree, UtensilsCrossed, Sparkles, Award, Gift, TrendingUp, ClipboardList, CalendarCheck, MapPin, BarChart3 } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';

const Sidebar = () => {
  const { t } = useTranslation();
  const pathname = usePathname();

  const navItems = [
    {
      href: '/admin/member-management',
      label: t('admin_member_management_title'),
      icon: Users
    },
    {
      href: '/admin/category-management',
      label: t('admin_category_management_title'),
      icon: FolderTree
    },
    {
      href: '/admin/menu-management',
      label: t('admin_menu_management_title'),
      icon: UtensilsCrossed
    },
    {
      href: '/admin/specials-management',
      label: t('admin_specials_management_title'),
      icon: Sparkles
    },
    {
      href: '/admin/orders-management',
      label: t('admin_orders_management', 'Orders Management'),
      icon: ClipboardList
    },
    {
      href: '/admin/reservations-management',
      label: t('admin_reservations_management', 'Reservations Management'),
      icon: CalendarCheck
    },
    {
      href: '/admin/table-layout-editor',
      label: t('table_layout_editor', 'Table Layout Editor'),
      icon: MapPin
    },
    {
      href: '/admin/table-statistics',
      label: t('table_statistics', 'Table Statistics'),
      icon: BarChart3
    },
    {
      href: '/admin/point-rules',
      label: 'Point Rules',
      icon: Award
    },
    {
      href: '/admin/customer-discounts',
      label: 'Customer Discounts',
      icon: Gift
    },
        {
      href: '/admin/fidelity-analytics',
      label: 'Fidelity Analytics',
      icon: TrendingUp
    },
  ];

  return (
    <aside className={styles.sidebar}>
      <div className={styles.sidebarTitle} suppressHydrationWarning>
        {t('admin_dashboard_title')}
      </div>
      <hr className={styles.sidebarDivider} />
      <nav>
        <ul>
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className={pathname.startsWith(item.href) ? styles.activeLink : ''}
                >
                  <Icon size={20} strokeWidth={2} />
                  <span suppressHydrationWarning>{item.label}</span>
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
