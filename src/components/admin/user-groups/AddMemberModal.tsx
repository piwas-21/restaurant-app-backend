import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, UserPlus } from 'lucide-react';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import modalStyles from './AddMemberModal.module.css';
import { fetchUsers } from '@/services/userService';
import { UserDto } from '@/types/user';

interface AddMemberModalProps {
  isOpen: boolean;
  onClose: () => void;
  onAddMember: (userId: string) => Promise<void>;
  isSubmitting: boolean;
}

const AddMemberModal: React.FC<AddMemberModalProps> = ({
  isOpen,
  onClose,
  onAddMember,
  isSubmitting
}) => {
  const { t } = useTranslation();
  const [searchQuery, setSearchQuery] = useState('');
  const [users, setUsers] = useState<UserDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      setSearchQuery('');
      setUsers([]);
      setSelectedUserId(null);
    }
  }, [isOpen]);

  const handleSearch = async () => {
    if (!searchQuery.trim()) return;
    
    setIsLoading(true);
    try {
      // Search for customers only
      const response = await fetchUsers('Customer', false, searchQuery, 1, 10);
      if (response.success && response.data) {
        setUsers(response.data.items);
      }
    } catch (error) {
      console.error('Failed to search users:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedUserId) {
      await onAddMember(selectedUserId);
      onClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div className={styles.modalOverlay}>
      <div className={styles.modalContent}>
        <h2>{t('add_member')}</h2>
        
        <div className={styles.formGroup}>
          <label>{t('search_and_select_customer')}</label>
          <div className={modalStyles.searchContainer}>
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={t('search_by_name_or_email')}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              className={modalStyles.searchInput}
            />
            <button 
              type="button" 
              onClick={handleSearch}
              className={`${styles.submitButton} ${modalStyles.searchButton}`}
              disabled={isLoading}
            >
              <Search size={18} />
            </button>
          </div>
        </div>

        <div className={modalStyles.userListContainer}>
          {isLoading ? (
            <div className={modalStyles.loadingContainer}>{t('loading')}</div>
          ) : users.length > 0 ? (
            <ul className={modalStyles.userList}>
              {users.map((user) => (
                <li 
                  key={user.id}
                  onClick={() => setSelectedUserId(user.id)}
                  className={modalStyles.userListItem}
                  style={{
                    backgroundColor: selectedUserId === user.id ? 'var(--success-color-dark)' : 'transparent'
                  }}
                >
                  <div>
                    <div className={modalStyles.userName}>{user.firstName} {user.lastName}</div>
                    <div className={modalStyles.userEmail}>{user.email}</div>
                  </div>
                  {selectedUserId === user.id && <UserPlus size={18} color="var(--success-color)" />}
                </li>
              ))}
            </ul>
          ) : searchQuery && !isLoading ? (
            <div className={modalStyles.emptyState}>
              {t('no_users_found_matching', { query: searchQuery })}
            </div>
          ) : (
            <div className={modalStyles.emptyState}>
              {t('search_orders')} {/* Reusing search placeholder text */}
            </div>
          )}
        </div>

        <div className={styles.buttonGroup}>
          <button 
            onClick={handleSubmit} 
            className={styles.submitButton} 
            disabled={!selectedUserId || isSubmitting}
          >
            {isSubmitting ? t('saving...') : t('add')}
          </button>
          <button 
            onClick={onClose} 
            className={styles.cancelButton}
            disabled={isSubmitting}
          >
            {t('cancel')}
          </button>
        </div>
      </div>
    </div>
  );
};

export default AddMemberModal;
