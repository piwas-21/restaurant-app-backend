'use client';

import React from 'react';
import styles from '@/app/styles/AdminPage.module.css';

interface PageHeaderProps {
  title: string;
  children?: React.ReactNode;
}

const PageHeader: React.FC<PageHeaderProps> = ({ title, children }) => {
  return (
    <div className={styles.pageHeader}>
      <h1 className={styles.pageTitle}>{title}</h1>
      {children && <div className={styles.pageActions}>{children}</div>}
    </div>
  );
};

export default PageHeader;
