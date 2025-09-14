'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';

interface MembersTableProps {
  users: any[];
  onEdit: (user: any) => void;
  onDelete: (user: any) => void;
}

const MembersTable: React.FC<MembersTableProps> = ({ users, onEdit, onDelete }) => {
  const { t } = useTranslation();

  return (
    <div className={styles.adminTableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('first_name')}</th>
            <th>{t('last_name')}</th>
            <th>{t('email_label')}</th>
            <th>{t('role')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {users.length > 0 ? (
            users.map((user: any) => (
              <tr key={user.id}>
                <td>{user.firstName}</td>
                <td>{user.lastName}</td>
                <td>{user.email}</td>
                <td>{user.role}</td>
                <td className={styles.actionsCell}>
                  <button
                    className={`${styles.adminButton} ${styles.edit}`}
                    onClick={() => onEdit(user)}
                  >
                    {t('edit')}
                  </button>
                  <button
                    className={`${styles.adminButton} ${styles.delete}`}
                    onClick={() => onDelete(user)}
                  >
                    {t('delete')}
                  </button>
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={5}>{t('no_users_found')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default MembersTable;
