"use client";

import React, { useState, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useRoleHelpers } from '@/hooks/useRoleHelpers';
import { UserCircle } from 'lucide-react';
import styles from '../app/styles/UserMenu.module.css';
import { useAuth } from './AuthContext';
import Link from 'next/link';

export default function UserMenu() {
  const { t } = useTranslation();
  const { getRoleLabel } = useRoleHelpers();
  const { user, logout } = useAuth();
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const toggleDropdown = () => {
    setDropdownOpen(!dropdownOpen);
  };

  const handleLogout = () => {
    logout();
    setDropdownOpen(false);
  };

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setDropdownOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, []);

  if (!user) {
    return null;
  }

  return (
    <div className={styles.userMenu} ref={dropdownRef}>
      <button
        onClick={toggleDropdown}
        className={styles.dropdownToggle}
        aria-haspopup="true"
        aria-expanded={dropdownOpen}
        aria-label={t('user_menu.aria_label')}
      >
        <UserCircle size={32} strokeWidth={1.5} className={styles.avatar} />
      </button>

      {dropdownOpen && (
        <div className={styles.dropdownMenu}>
          <div className={styles.userInfo}>
            <p className={styles.userName}>{user.firstName} {user.lastName}</p>
            <p className={styles.userRole}>{getRoleLabel(user.role)}</p>
          </div>
          {user.role.toLowerCase() === 'customer' && (
            <>
              <Link href="/account" className={styles.dropdownLink} onClick={() => setDropdownOpen(false)}>
                {t('user_menu.my_account', 'My Account')}
              </Link>
              <Link href="/orders" className={styles.dropdownLink} onClick={() => setDropdownOpen(false)}>
                {t('user_menu.my_orders', 'My Orders')}
              </Link>
              <Link href="/my-reservations" className={styles.dropdownLink} onClick={() => setDropdownOpen(false)}>
                {t('nav_reservations', 'Reservations')}
              </Link>
            </>
          )}
          <button onClick={handleLogout} className={styles.logoutButton}>
            {t('user_menu.logout')}
          </button>
        </div>
      )}
    </div>
  );
}
