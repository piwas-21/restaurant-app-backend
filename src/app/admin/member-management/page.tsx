'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useMemberManagement } from '@/hooks/useMemberManagement';
import styles from '@/app/styles/AdminPage.module.css';
import RegisterStaffModal from '@/components/admin/RegisterStaffModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import Pagination from '@/components/common/Pagination';
import PageHeader from '@/components/admin/PageHeader';
import FilterControls from '@/components/admin/member-management/FilterControls';
import MembersTable from '@/components/admin/member-management/MembersTable';
import UserStatistics from '@/components/admin/member-management/UserStatistics';
import EditUserModal from '@/components/admin/member-management/EditUserModal';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import type { UserDto } from '@/types/user';

const MemberManagementPage = () => {
  const { t } = useTranslation();
  const {
    users,
    totalCount,
    isLoading,
    error,
    getUsers,
    handleDeleteUser,
    handleUpdateUser,
  } = useMemberManagement();

  const [activeTab, setActiveTab] = useState('customers');
  const [searchTerm, setSearchTerm] = useState('');
  const [showDeleted, setShowDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(10);

  const [isRegisterModalOpen, setIsRegisterModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [userToEdit, setUserToEdit] = useState<UserDto | null>(null);
  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [userToDelete, setUserToDelete] = useState<UserDto | null>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);
  const [statsKey, setStatsKey] = useState(0); // Key to force statistics refresh

  // Reset showDeleted when switching to staff tab
  useEffect(() => {
    if (activeTab === 'staff') {
      setShowDeleted(false);
    }
  }, [activeTab]);

  useEffect(() => {
    const role = activeTab === 'customers' ? 'Customer' : '';
    // For staff, always pass false for showDeleted since they are hard deleted
    const showDeletedParam = activeTab === 'customers' ? showDeleted : false;
    getUsers(role, showDeletedParam, searchTerm, page, pageSize);
  }, [activeTab, searchTerm, showDeleted, page, pageSize, getUsers]);

  const handleEdit = (user: UserDto) => {
    setUserToEdit(user);
    setIsEditModalOpen(true);
  };

  const handleSaveUser = async (updatedUser: Partial<UserDto>) => {
    if (!userToEdit) return;

    try {
      // Extract password if it exists
      const { password, ...updates } = updatedUser as Partial<UserDto> & { password?: string };

      // Call the update API
      const result = await handleUpdateUser(userToEdit, updates, password);

      // Show result
      setIsEditModalOpen(false);
      setUserToEdit(null);
      setResultModalMessage(t(result.message || 'User updated successfully'));
      setIsResultModalSuccess(result.success);
      setIsResultModalOpen(true);

      // Refresh the user list
      const role = activeTab === 'customers' ? 'Customer' : '';
      const showDeletedParam = activeTab === 'customers' ? showDeleted : false;
      await getUsers(role, showDeletedParam, searchTerm, page, pageSize);

      // Refresh statistics
      setStatsKey(prev => prev + 1);
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error updating user:', error);
      setResultModalMessage(t('error_updating_user', 'Error updating user'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    }
  };

  const handleDeleteClick = (user: any) => {
    setUserToDelete(user);
    setIsConfirmationModalOpen(true);
  };

  const getDeleteConfirmationMessage = () => {
    if (!userToDelete) return '';

    const userName = `${userToDelete.firstName} ${userToDelete.lastName}`;

    // Staff members are permanently deleted
    if (activeTab === 'staff') {
      return t('delete_staff_confirmation_message',
        `Are you sure you want to permanently delete ${userName}? This action cannot be undone.`);
    }

    // Customers are soft deleted
    return t('delete_customer_confirmation_message',
      `Are you sure you want to delete ${userName}? This customer can be restored later if needed.`);
  };

  const handleConfirmDelete = async () => {
    if (userToDelete) {
      const result = await handleDeleteUser(userToDelete.id);
      setIsConfirmationModalOpen(false);
      setResultModalMessage(t(result.message || ''));
      setIsResultModalSuccess(result.success);
      setIsResultModalOpen(true);
      setUserToDelete(null);

      // Refresh the user list after delete
      if (result.success) {
        const role = activeTab === 'customers' ? 'Customer' : '';
        const showDeletedParam = activeTab === 'customers' ? showDeleted : false;
        await getUsers(role, showDeletedParam, searchTerm, page, pageSize);

        // Refresh statistics
        setStatsKey(prev => prev + 1);
      }
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <AdminAuthGuard>
      <div className={styles.adminContainer}>
        <PageHeader title={t('admin_member_management_title')}>
          <button className={`${styles.adminButton} ${styles.add}`} onClick={() => setIsRegisterModalOpen(true)}>
            {t('register_staff')}
          </button>
        </PageHeader>
        <div className={styles.adminContent}>
          <UserStatistics key={statsKey} />
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
            isLoading={isLoading}
          />
          <Pagination
            currentPage={page}
            totalPages={totalPages}
            onPageChange={setPage}
            isLoading={isLoading}
          />
        </div>
      </div>
      <RegisterStaffModal
        isOpen={isRegisterModalOpen}
        onClose={() => setIsRegisterModalOpen(false)}
        onStaffRegistered={() => {
          const role = activeTab === 'customers' ? 'Customer' : '';
          const showDeletedParam = activeTab === 'customers' ? showDeleted : false;
          getUsers(role, showDeletedParam, searchTerm, page, pageSize);
          setStatsKey(prev => prev + 1); // Refresh statistics
        }}
      />
      <ConfirmationModal
        isOpen={isConfirmationModalOpen}
        onClose={() => setIsConfirmationModalOpen(false)}
        onConfirm={handleConfirmDelete}
        message={getDeleteConfirmationMessage()}
      />
      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
      <EditUserModal
        isOpen={isEditModalOpen}
        user={userToEdit}
        onClose={() => {
          setIsEditModalOpen(false);
          setUserToEdit(null);
        }}
        onSave={handleSaveUser}
      />
    </AdminAuthGuard>
  );
};

export default MemberManagementPage;
