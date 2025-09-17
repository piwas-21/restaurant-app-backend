'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';

const Sidebar = () => {
  const { t } = useTranslation();
  const pathname = usePathname();

  const navItems = [
    { href: '/admin/member-management', label: t('admin_member_management_title') },
    { href: '/admin/category-management', label: t('admin_category_management_title') },
    { href: '/admin/menu-management', label: t('admin_menu_management_title') },
    { href: '/admin/specials-management', label: t('admin_specials_management_title') },
  ];

  return (
    <aside className={styles.sidebar}>
      <div className={styles.sidebarTitle}>{t('admin_dashboard_title')}</div>
      <hr className={styles.sidebarDivider} />
      <nav>
        <ul>
          {navItems.map((item) => (
            <li key={item.href}>
              <Link href={item.href} className={pathname.startsWith(item.href) ? styles.activeLink : ''}>
                {item.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  );
};

export default Sidebar;
