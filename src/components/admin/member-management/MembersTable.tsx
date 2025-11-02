'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { Phone, Calendar, CheckCircle, XCircle } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';
import tableStyles from './MembersTable.module.css';
import type { UserDto } from '@/types/user';

interface MembersTableProps {
  users: UserDto[];
  onEdit: (user: UserDto) => void;
  onDelete: (user: UserDto) => void;
  isLoading?: boolean;
}

const MembersTable: React.FC<MembersTableProps> = ({ users, onEdit, onDelete, isLoading = false }) => {
  const { t } = useTranslation();

  const formatDate = (dateString: string | undefined) => {
    if (!dateString) return '-';
    try {
      return new Date(dateString).toLocaleDateString();
    } catch {
      return '-';
    }
  };

  if (isLoading) {
    return (
      <div className={styles.adminTableContainer}>
        <table className={styles.adminTable}>
          <thead>
            <tr>
              <th>{t('first_name')}</th>
              <th>{t('last_name')}</th>
              <th>{t('email_label')}</th>
              <th>{t('phone_label')}</th>
              <th>{t('role')}</th>
              <th>{t('status')}</th>
              <th>{t('created_at')}</th>
              <th>{t('actions_header')}</th>
            </tr>
          </thead>
          <tbody>
            {[...Array(5)].map((_, index) => (
              <tr key={index}>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
                <td><div className={tableStyles.skeleton} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  return (
    <div className={styles.adminTableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('first_name')}</th>
            <th>{t('last_name')}</th>
            <th>{t('email_label')}</th>
            <th>{t('phone_label')}</th>
            <th>{t('role')}</th>
            <th>{t('status')}</th>
            <th>{t('created_at')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {users.length > 0 ? (
            users.map((user: UserDto) => (
              <tr key={user.id}>
                <td>{user.firstName}</td>
                <td>{user.lastName}</td>
                <td>{user.email}</td>
                <td>
                  <div className={tableStyles.phoneCell}>
                    {user.phoneNumber ? (
                      <>
                        <Phone size={14} />
                        <span>{user.phoneNumber}</span>
                      </>
                    ) : (
                      <span className={tableStyles.noData}>-</span>
                    )}
                  </div>
                </td>
                <td>
                  <span className={`${tableStyles.roleBadge} ${tableStyles[user.role.toLowerCase()]}`}>
                    {user.role}
                  </span>
                </td>
                <td>
                  {user.isDeleted ? (
                    <span className={`${tableStyles.statusBadge} ${tableStyles.deleted}`}>
                      <XCircle size={14} />
                      {t('deleted')}
                    </span>
                  ) : (
                    <span className={`${tableStyles.statusBadge} ${tableStyles.active}`}>
                      <CheckCircle size={14} />
                      {t('active')}
                    </span>
                  )}
                </td>
                <td>
                  <div className={tableStyles.dateCell}>
                    <Calendar size={14} />
                    <span>{formatDate(user.createdAt)}</span>
                  </div>
                </td>
                <td className={styles.actionsCell}>
                  <button
                    className={`${styles.adminButton} ${styles.edit}`}
                    onClick={() => onEdit(user)}
                    disabled={user.role === 'Customer'}
                    title={user.role === 'Customer' ? t('customers_edit_own_profile', 'Customers can only edit their own profile') : t('edit')}
                  >
                    {t('edit')}
                  </button>
                  <button
                    className={`${styles.adminButton} ${styles.delete}`}
                    onClick={() => onDelete(user)}
                    disabled={user.isDeleted}
                  >
                    {t('delete')}
                  </button>
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={8}>{t('no_users_found')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default MembersTable;
