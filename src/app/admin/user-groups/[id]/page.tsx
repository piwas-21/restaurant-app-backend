'use client';

import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from './UserGroupDetailsPage.module.css';
import PageHeader from '@/components/admin/PageHeader';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import MembersTable from '@/components/admin/user-groups/MembersTable';
import AddMemberModal from '@/components/admin/user-groups/AddMemberModal';
import DiscountsTable from '@/components/admin/user-groups/DiscountsTable';
import DiscountModal from '@/components/admin/user-groups/DiscountModal';
import QRCodeModal from '@/components/admin/user-groups/QRCodeModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import {
  UserGroupDto,
  GroupMembershipDto,
  GroupDiscountDto,
  CreateGroupDiscountDto,
  UpdateGroupDiscountDto
} from '@/types/userGroupTypes';
import {
  getUserGroup,
  getGroupMembers,
  addGroupMember,
  removeGroupMember,
  getGroupDiscounts,
  createGroupDiscount,
  updateGroupDiscount,
  deleteGroupDiscount
} from '@/services/userGroupService';

const UserGroupDetailsPage = () => {
  const { t } = useTranslation();
  const router = useRouter();
  const params = useParams();
  const groupId = params.id as string;

  const [group, setGroup] = useState<UserGroupDto | null>(null);
  const [members, setMembers] = useState<GroupMembershipDto[]>([]);
  const [discounts, setDiscounts] = useState<GroupDiscountDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const [isAddMemberModalOpen, setIsAddMemberModalOpen] = useState(false);
  const [isDiscountModalOpen, setIsDiscountModalOpen] = useState(false);
  const [selectedDiscount, setSelectedDiscount] = useState<GroupDiscountDto | null>(null);

  const [isQRCodeModalOpen, setIsQRCodeModalOpen] = useState(false);
  const [qrCodeData, setQrCodeData] = useState<{ data: string; title: string } | null>(null);

  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [itemToDelete, setItemToDelete] = useState<{ type: 'member' | 'discount', id: string, name: string } | null>(null);

  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const fetchData = useCallback(async () => {
    if (!groupId) return;
    setIsLoading(true);
    try {
      const [groupRes, membersRes, discountsRes] = await Promise.all([
        getUserGroup(groupId),
        getGroupMembers(groupId),
        getGroupDiscounts(groupId)
      ]);

      if (groupRes.success && groupRes.data) setGroup(groupRes.data);

      if (membersRes.success && membersRes.data) setMembers(membersRes.data);
      else setMembers([]);

      if (discountsRes.success && discountsRes.data) setDiscounts(discountsRes.data);
      else setDiscounts([]);

    } catch (err) {
      console.error('Failed to fetch group details:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    } finally {
      setIsLoading(false);
    }
  }, [groupId, t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleAddMember = async (userId: string) => {
    setIsSubmitting(true);
    try {
      const response = await addGroupMember(groupId, { userId });
      // @ts-expect-error - Response structure handling
      if (response.success || response.id) {
        setResultModalMessage(t('success'));
        setIsResultModalSuccess(true);
        setIsResultModalOpen(true);
        fetchData();
      } else {
        throw new Error('Failed to add member');
      }
    } catch (err) {
      console.error('Error adding member:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleRemoveMemberClick = (member: GroupMembershipDto) => {
    setItemToDelete({ type: 'member', id: member.userId, name: member.userName });
    setIsConfirmationModalOpen(true);
  };

  const handleDiscountSubmit = async (data: any) => {
    setIsSubmitting(true);
    try {
      let response;
      if (selectedDiscount) {
        const updateDto: UpdateGroupDiscountDto = {
          id: selectedDiscount.id,
          ...data
        };
        response = await updateGroupDiscount(selectedDiscount.id, updateDto);
      } else {
        const createDto: CreateGroupDiscountDto = {
          ...data
        };
        response = await createGroupDiscount(groupId, createDto);
      }

      // @ts-expect-error - Response structure handling
      if (response.success || response.id) {
        setResultModalMessage(t('success'));
        setIsResultModalSuccess(true);
        setIsResultModalOpen(true);
        setIsDiscountModalOpen(false);
        fetchData();
      } else {
        throw new Error('Failed to save discount');
      }
    } catch (err) {
      console.error('Error saving discount:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
      setIsResultModalOpen(true);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDeleteDiscountClick = (discount: GroupDiscountDto) => {
    setItemToDelete({ type: 'discount', id: discount.id, name: discount.name });
    setIsConfirmationModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!itemToDelete) return;
    try {
      let response;
      if (itemToDelete.type === 'member') {
        response = await removeGroupMember(groupId, itemToDelete.id);
      } else {
        response = await deleteGroupDiscount(itemToDelete.id);
      }

      // @ts-expect-error - Response structure handling
      if (response.success || response === true) {
        setResultModalMessage(t('success'));
        setIsResultModalSuccess(true);
        fetchData();
      } else {
        throw new Error('Failed to delete item');
      }
    } catch (err) {
      console.error('Error deleting item:', err);
      setResultModalMessage(t('error'));
      setIsResultModalSuccess(false);
    } finally {
      setIsConfirmationModalOpen(false);
      setIsResultModalOpen(true);
      setItemToDelete(null);
    }
  };

  const handleViewMemberQRCode = (member: GroupMembershipDto) => {
    setQrCodeData({
      data: member.uniqueQRCode,
      title: `${member.userName} - ${group?.name}`
    });
    setIsQRCodeModalOpen(true);
  };

  return (
    <AdminAuthGuard>
      <div className={styles.adminContainer}>
        <PageHeader title={group ? group.name : t('loading')}>
          <button
            className={`${styles.adminButton} ${detailsStyles.backButton}`}
            onClick={() => router.push('/admin/user-groups')}
          >
            <ArrowLeft size={18} />
            {t('back_to_groups')}
          </button>
        </PageHeader>

        <div className={styles.adminContent}>
          {group && (
            <div className={detailsStyles.groupInfoCard}>
              <h3>{t('group_info')}</h3>
              <p><strong>{t('description')}:</strong> {group.description}</p>
              <p><strong>{t('valid_from')}:</strong> {group.validFrom ? new Date(group.validFrom).toLocaleDateString() : '-'}</p>
              <p><strong>{t('valid_until')}:</strong> {group.validUntil ? new Date(group.validUntil).toLocaleDateString() : '-'}</p>
              <p><strong>{t('active_status')}:</strong> {group.isActive ? t('active') : t('inactive')}</p>
            </div>
          )}

          <div className={detailsStyles.section}>
            <div className={detailsStyles.sectionHeader}>
              <h3>{t('members')}</h3>
              <button
                className={`${styles.adminButton} ${styles.add}`}
                onClick={() => setIsAddMemberModalOpen(true)}
              >
                {t('add_member')}
              </button>
            </div>
            <MembersTable
              members={members}
              isLoading={isLoading}
              onRemove={handleRemoveMemberClick}
              onViewQRCode={handleViewMemberQRCode}
            />
          </div>

          <div className={detailsStyles.section}>
            <div className={detailsStyles.sectionHeader}>
              <h3>{t('discounts')}</h3>
              <button
                className={`${styles.adminButton} ${styles.add}`}
                onClick={() => {
                  setSelectedDiscount(null);
                  setIsDiscountModalOpen(true);
                }}
              >
                {t('add_discount')}
              </button>
            </div>
            <DiscountsTable
              discounts={discounts}
              isLoading={isLoading}
              onEdit={(discount) => {
                setSelectedDiscount(discount);
                setIsDiscountModalOpen(true);
              }}
              onDelete={handleDeleteDiscountClick}
            />
          </div>
        </div>
      </div>

      <AddMemberModal
        isOpen={isAddMemberModalOpen}
        onClose={() => setIsAddMemberModalOpen(false)}
        onAddMember={handleAddMember}
        isSubmitting={isSubmitting}
      />

      <DiscountModal
        isOpen={isDiscountModalOpen}
        onClose={() => setIsDiscountModalOpen(false)}
        onSubmit={handleDiscountSubmit}
        initialData={selectedDiscount}
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
        message={t('delete_confirmation')}
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

export default UserGroupDetailsPage;
