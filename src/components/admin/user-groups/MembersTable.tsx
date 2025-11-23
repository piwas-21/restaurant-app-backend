import React from 'react';
import { useTranslation } from 'react-i18next';
import { GroupMembershipDto } from '@/types/userGroupTypes';
import { Trash2, QrCode } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';

interface MembersTableProps {
  members: GroupMembershipDto[];
  isLoading: boolean;
  onRemove: (member: GroupMembershipDto) => void;
  onViewQRCode: (member: GroupMembershipDto) => void;
}

const MembersTable: React.FC<MembersTableProps> = ({
  members,
  isLoading,
  onRemove,
  onViewQRCode
}) => {
  const { t } = useTranslation();

  if (isLoading) {
    return <div className={styles.loading}>{t('loading')}</div>;
  }

  if (members.length === 0) {
    return <div className={styles.noData}>{t('no_users_found')}</div>;
  }

  return (
    <div className={styles.tableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('name')}</th>
            <th>{t('customer_email')}</th>
            <th>{t('joined_at')}</th>
            <th>{t('expires_at')}</th>
            <th>{t('status')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {members.map((member) => (
            <tr key={member.id}>
              <td>{member.userName}</td>
              <td>{member.userEmail}</td>
              <td>{new Date(member.joinedAt).toLocaleDateString()}</td>
              <td>{member.expiresAt ? new Date(member.expiresAt).toLocaleDateString() : '-'}</td>
              <td>
                <span className={member.isActive ? styles.statusActive : styles.statusInactive}>
                  {member.isActive ? t('active') : t('inactive')}
                </span>
              </td>
              <td className={styles.actionsCell}>
                <button
                  className={styles.actionButton}
                  onClick={() => onViewQRCode(member)}
                  title={t('qr_code')}
                >
                  <QrCode size={18} />
                </button>
                <button
                  className={`${styles.actionButton} ${styles.deleteButton}`}
                  onClick={() => onRemove(member)}
                  title={t('remove_member')}
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

export default MembersTable;
