'use client';

import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import styles from '@/app/styles/AdminPage.module.css';
import PageHeader from '@/components/admin/PageHeader';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import UserGroupsTable from '@/components/admin/user-groups/UserGroupsTable';
import UserGroupModal from '@/components/admin/user-groups/UserGroupModal';
import QRCodeModal from '@/components/admin/user-groups/QRCodeModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import { UserGroupDto, CreateUserGroupDto, UpdateUserGroupDto } from '@/types/userGroupTypes';
import { getUserGroups, createUserGroup, updateUserGroup, deleteUserGroup, createGroupDiscount } from '@/services/userGroupService';

const UserGroupsPage = () => {
  const { t } = useTranslation();
  const router = useRouter();
  
  const [groups, setGroups] = useState<UserGroupDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedGroup, setSelectedGroup] = useState<UserGroupDto | null>(null);
  
  const [isQRCodeModalOpen, setIsQRCodeModalOpen] = useState(false);
  const [qrCodeData, setQrCodeData] = useState<{ data: string; title: string } | null>(null);
  
  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [groupToDelete, setGroupToDelete] = useState<UserGroupDto | null>(null);
  
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const fetchGroups = useCallback(async () => {
    setIsLoading(true);
    try {
      const response = await getUserGroups();
      if (response.success && response.data) {
        setGroups(response.data);
      } else {
        setGroups([]);
      }
      setError(null);
    } catch (err) {
      console.error('Failed to fetch user groups:', err);
      setError('Failed to load user groups');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchGroups();
  }, [fetchGroups]);

  const handleCreateGroup = async (data: any) => {
    setIsSubmitting(true);
    try {
      const createDto: CreateUserGroupDto = {
        name: data.name,
        description: data.description || '',
        validFrom: data.validFrom || undefined,
        validUntil: data.validUntil || undefined,
      };

      if (data.hasInitialDiscount) {
        createDto.initialDiscount = {
          name: data.discountName,
          type: data.discountType,
          value: data.discountValue,
          minimumOrderAmount: data.minOrderAmount,
          maximumDiscountAmount: data.maxDiscountAmount
        };
      }

      const response = await createUserGroup(createDto);
      if (response.success) {
        setResultModalMessage(t('user_group_created_success'));
        setIsResultModalSuccess(true);
        setIsResultModalOpen(true);
        setIsCreateModalOpen(false);
        fetchGroups();
      } else {
        throw new Error(response.message || 'Failed to create group');
      }
    } catch (err) {
      console.error('Error creating group:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleUpdateGroup = async (data: any) => {
    if (!selectedGroup) return;
    setIsSubmitting(true);
    try {
      const updateDto: UpdateUserGroupDto = {
        id: selectedGroup.id,
        name: data.name,
        description: data.description || '',
        isActive: data.isActive,
        validFrom: data.validFrom || undefined,
        validUntil: data.validUntil || undefined,
      };

      const response = await updateUserGroup(selectedGroup.id, updateDto);
      if (response.success) {
        setResultModalMessage(t('user_group_updated_success'));
        setIsResultModalSuccess(true);
        setIsResultModalOpen(true);
        setIsEditModalOpen(false);
        fetchGroups();
      } else {
        throw new Error(response.message || 'Failed to update group');
      }
    } catch (err) {
      console.error('Error updating group:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDeleteClick = (group: UserGroupDto) => {
    setGroupToDelete(group);
    setIsConfirmationModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!groupToDelete) return;
    try {
      const response = await deleteUserGroup(groupToDelete.id);
      if (response.success) {
        setResultModalMessage(t('user_group_deleted_success'));
        setIsResultModalSuccess(true);
        fetchGroups();
      } else {
        throw new Error(response.message || 'Failed to delete group');
      }
    } catch (err) {
      console.error('Error deleting group:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
    } finally {
      setIsConfirmationModalOpen(false);
      setIsResultModalOpen(true);
      setGroupToDelete(null);
    }
  };

  const handleEditClick = (group: UserGroupDto) => {
    setSelectedGroup(group);
    setIsEditModalOpen(true);
  };

  const handleManageMembers = (group: UserGroupDto) => {
    router.push(`/admin/user-groups/${group.id}`);
  };

  const handleViewQRCode = (group: UserGroupDto) => {
    setQrCodeData({
      data: group.qrCodeData,
      title: group.name
    });
    setIsQRCodeModalOpen(true);
  };

  return (
    <AdminAuthGuard>
      <div className={styles.adminContainer}>
        <PageHeader title={t('admin_user_groups_title')}>
          <button 
            className={`${styles.adminButton} ${styles.add}`} 
            onClick={() => setIsCreateModalOpen(true)}
          >
            {t('create_user_group')}
          </button>
        </PageHeader>
        
        <div className={styles.adminContent}>
          <UserGroupsTable
            groups={groups}
            isLoading={isLoading}
            onEdit={handleEditClick}
            onDelete={handleDeleteClick}
            onManageMembers={handleManageMembers}
            onViewQRCode={handleViewQRCode}
          />
        </div>
      </div>

      <UserGroupModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onSubmit={handleCreateGroup}
        isSubmitting={isSubmitting}
      />

      <UserGroupModal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        onSubmit={handleUpdateGroup}
        initialData={selectedGroup}
        isSubmitting={isSubmitting}
      />

      <QRCodeModal
        isOpen={isQRCodeModalOpen}
        onClose={() => setIsQRCodeModalOpen(false)}
        qrCodeData={qrCodeData?.data || ''}
        title={qrCodeData?.title || ''}
      />

      <ConfirmationModal
        isOpen={isConfirmationModalOpen}
        onClose={() => setIsConfirmationModalOpen(false)}
        onConfirm={handleConfirmDelete}
        message={t('delete_user_group_confirmation')}
      />

      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
    </AdminAuthGuard>
  );
};

export default UserGroupsPage;
