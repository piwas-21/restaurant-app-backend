import React from 'react';
import { useTranslation } from 'react-i18next';
import { UserGroupDto } from '@/types/userGroupTypes';
import { Edit, Trash2, Users, QrCode } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';

interface UserGroupsTableProps {
  groups: UserGroupDto[];
  isLoading: boolean;
  onEdit: (group: UserGroupDto) => void;
  onDelete: (group: UserGroupDto) => void;
  onManageMembers: (group: UserGroupDto) => void;
  onViewQRCode: (group: UserGroupDto) => void;
}

const UserGroupsTable: React.FC<UserGroupsTableProps> = ({
  groups,
  isLoading,
  onEdit,
  onDelete,
  onManageMembers,
  onViewQRCode
}) => {
  const { t } = useTranslation();

  if (isLoading) {
    return <div className={styles.loading}>{t('loading')}</div>;
  }

  if (groups.length === 0) {
    return <div className={styles.noData}>{t('no_user_groups_found')}</div>;
  }

  return (
    <div className={styles.tableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('group_name')}</th>
            <th>{t('group_description')}</th>
            <th>{t('valid_from')}</th>
            <th>{t('valid_until')}</th>
            <th>{t('member_count')}</th>
            <th>{t('status')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {groups.map((group) => (
            <tr key={group.id}>
              <td>{group.name}</td>
              <td>{group.description}</td>
              <td>{group.validFrom ? new Date(group.validFrom).toLocaleDateString() : '-'}</td>
              <td>{group.validUntil ? new Date(group.validUntil).toLocaleDateString() : '-'}</td>
              <td>{group.memberCount}</td>
              <td>
                <span className={group.isActive ? styles.statusActive : styles.statusInactive}>
                  {group.isActive ? t('active') : t('inactive')}
                </span>
              </td>
              <td className={styles.actionsCell}>
                <button
                  className={styles.actionButton}
                  onClick={() => onManageMembers(group)}
                  title={t('view_members')}
                >
                  <Users size={18} />
                </button>
                <button
                  className={styles.actionButton}
                  onClick={() => onViewQRCode(group)}
                  title={t('qr_code')}
                >
                  <QrCode size={18} />
                </button>
                <button
                  className={styles.actionButton}
                  onClick={() => onEdit(group)}
                  title={t('edit')}
                >
                  <Edit size={18} />
                </button>
                <button
                  className={`${styles.actionButton} ${styles.deleteButton}`}
                  onClick={() => onDelete(group)}
                  title={t('delete')}
                >
                  <Trash2 size={18} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default UserGroupsTable;
