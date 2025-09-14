'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useMemberManagement } from '@/hooks/useMemberManagement';
import styles from '@/app/styles/AdminPage.module.css';
import RegisterStaffModal from '@/components/admin/RegisterStaffModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import MemberManagementHeader from '@/components/admin/member-management/MemberManagementHeader';
import FilterControls from '@/components/admin/member-management/FilterControls';
import MembersTable from '@/components/admin/member-management/MembersTable';

const MemberManagementPage = () => {
  const { t } = useTranslation();
  const {
    users,
    totalCount,
    isLoading,
    error,
    getUsers,
    handleDeleteUser,
  } = useMemberManagement();

  const [activeTab, setActiveTab] = useState('customers');
  const [searchTerm, setSearchTerm] = useState('');
  const [showDeleted, setShowDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(10);

  const [isRegisterModalOpen, setIsRegisterModalOpen] = useState(false);
  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [userToDelete, setUserToDelete] = useState<any>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  useEffect(() => {
    const role = activeTab === 'customers' ? 'Customer' : '';
    getUsers(role, showDeleted, searchTerm, page, pageSize);
  }, [activeTab, searchTerm, showDeleted, page, pageSize, getUsers]);

  const handleEdit = (user: any) => {
    // Handle edit logic
  };

  const handleDeleteClick = (user: any) => {
    setUserToDelete(user);
    setIsConfirmationModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (userToDelete) {
      const result = await handleDeleteUser(userToDelete.id);
      setIsConfirmationModalOpen(false);
      setResultModalMessage(t(result.message || ''));
      setIsResultModalSuccess(result.success);
      setIsResultModalOpen(true);
      setUserToDelete(null);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <>
      <div className={styles.adminContainer}>
        <MemberManagementHeader onRegisterStaff={() => setIsRegisterModalOpen(true)} />
        <div className={styles.adminContent}>
          <FilterControls
            activeTab={activeTab}
            setActiveTab={setActiveTab}
            searchTerm={searchTerm}
            setSearchTerm={setSearchTerm}
            showDeleted={showDeleted}
            setShowDeleted={setShowDeleted}
          />
          {error && <p className={styles.error}>{error}</p>}
          <MembersTable
            users={users}
            onEdit={handleEdit}
            onDelete={handleDeleteClick}
          />
          <div className={styles.pagination}>
            <button onClick={() => setPage(page - 1)} disabled={page === 1}>
              {t('previous')}
            </button>
            <span>
              Page {page} of {totalPages}
            </span>
            <button
              onClick={() => setPage(page + 1)}
              disabled={page === totalPages}
            >
              {t('next')}
            </button>
          </div>
        </div>
      </div>
      <RegisterStaffModal 
        isOpen={isRegisterModalOpen}
        onClose={() => setIsRegisterModalOpen(false)}
        onStaffRegistered={() => getUsers(activeTab === 'customers' ? 'Customer' : '', showDeleted, searchTerm, page, pageSize)}
      />
      <ConfirmationModal
        isOpen={isConfirmationModalOpen}
        onClose={() => setIsConfirmationModalOpen(false)}
        onConfirm={handleConfirmDelete}
        message={t('delete_user_confirmation_message', { name: userToDelete?.firstName + ' ' + userToDelete?.lastName })}
      />
      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
    </>
  );
};

export default MemberManagementPage;
