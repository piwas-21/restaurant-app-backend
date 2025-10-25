"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import styles from '../styles/AccountPage.module.css';

import PersonalInfoSection from '../../components/account/PersonalInfoSection';
import PasswordManagementSection from '../../components/account/PasswordManagementSection';
import OrderHistorySection from '../../components/account/OrderHistorySection';
import FidelityPointsSection from '../../components/account/FidelityPointsSection';
import AddressManagement from '../../components/account/AddressManagement';
import { getMyOrders } from '@/services/orderService';
import { getCurrentUser, updateProfile, type UpdateUserProfileCommand } from '@/services/userService';
import { changePassword } from '@/services/authService';
import { OrderDto } from '@/types/order';

export interface UserProfile {
  fullName: string;
  email: string;
  phoneNumber: string;
}

export type OrderStatus = "Delivered" | "In Progress" | "Cancelled" | "Pending Payment" | "Pending" | "Confirmed" | "Preparing" | "Ready" | "InTransit" | "Completed";

export interface OrderItemDetail {
  id: string;
  name: string;
  quantity: number;
  price: number; // Price per unit when ordered
}

export interface OrderHistoryItem {
  id: string;
  date: string;
  status: OrderStatus;
  totalAmount: number;
  orderType: string;
  items: OrderItemDetail[];
  deliveryAddress?: {
    addressLine1: string;
    addressLine2?: string;
    city: string;
    state?: string;
    postalCode: string;
    country: string;
  };
}

export type ProfileErrorKeys =
  | "fullName"
  | "email"
  | "phoneNumber"
  | "form";

const mockUserProfile: UserProfile = {
  fullName: "Jane Doe",
  email: "jane.doe@example.com",
  phoneNumber: "+41 79 123 45 67",
};

export default function AccountPage() {
  const { t } = useTranslation();
  const router = useRouter();

  // Check authentication
  useEffect(() => {
    const token = localStorage.getItem('auth_token');
    if (!token) {
      router.push('/auth/login?redirect=/account');
    }
  }, [router]);

  const [profile, setProfile] = useState<UserProfile>(mockUserProfile);
  const [profileErrors, setProfileErrors] = useState<Partial<Record<ProfileErrorKeys, string>>>({});
  const [profileSuccess, setProfileSuccess] = useState<string>("");

  // Load user profile on mount
  useEffect(() => {
    const loadProfile = async () => {
      try {
        const userData = await getCurrentUser();
        setProfile({
          fullName: userData.fullName || `${userData.firstName} ${userData.lastName}`,
          email: userData.email,
          phoneNumber: userData.phoneNumber || '',
        });
      } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to load profile:', error);
      }
    };
    loadProfile();
  }, []);

  const handleProfileChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    const { name, value } = e.target;
    setProfile(prev => ({ ...prev, [name]: value }));
    if (profileErrors[name as ProfileErrorKeys] || profileErrors.form) {
      setProfileErrors(prev => ({ ...prev, [name as ProfileErrorKeys]: undefined, form: undefined }));
    }
    setProfileSuccess("");
  };

  const validateProfile = (): boolean => {
    const errors: Partial<Record<ProfileErrorKeys, string>> = {};
    if (!profile.fullName.trim()) {
      errors.fullName = t('field_required_error', { fieldName: t('full_name_label', 'Full Name') });
    }
    // Email is readonly, no validation needed
    if (!profile.phoneNumber.trim()) {
      errors.phoneNumber = t('field_required_error', { fieldName: t('customer_phone_label', 'Phone Number') });
    }
    setProfileErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleProfileSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setProfileSuccess("");
    setProfileErrors({});
    if (!validateProfile()) return;

    try {
      // Extract first and last name from full name
      const names = profile.fullName.trim().split(' ');
      const firstName = names[0] || '';
      const lastName = names.slice(1).join(' ') || '';

      const command: UpdateUserProfileCommand = {
        firstName,
        lastName,
        phoneNumber: profile.phoneNumber || undefined,
      };

      await updateProfile(command);
      setProfileSuccess(t('changes_saved_success', 'Your information has been updated!'));
    } catch (error: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to update profile:', error);
      setProfileErrors({ form: error.message || t('profile_update_error', 'Failed to update profile. Please try again.') });
    }
  };

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [passwordErrors, setPasswordErrors] = useState<Partial<Record<string, string>>>({});
  const [passwordSuccess, setPasswordSuccess] = useState<string>("");
  const [passwordStrength, setPasswordStrength] = useState(0);
  const [passwordStrengthText, setPasswordStrengthText] = useState<string>("");

  const checkPasswordStrength = (password: string): { strength: number; text: string } => {
    let score = 0;
    if (!password || password.length < 8) score = 0;
    else {
        if (password.length >= 8) score++;
        if (/[A-Z]/.test(password)) score++;
        if (/[a-z]/.test(password)) score++;
        if (/[0-9]/.test(password)) score++;
        if (/[^A-Za-z0-9]/.test(password)) score++;
    }
    if (score === 0 && password.length > 0 && password.length <8) return { strength: 1, text: t('password_strength_weak', 'Weak') };
    if (score <= 2 && password.length >=8) return { strength: 1, text: t('password_strength_weak', 'Weak') };
    if (score <= 4 && password.length >=8) return { strength: 2, text: t('password_strength_medium', 'Medium') };
    if (score >= 5 && password.length >=8) return { strength: 3, text: t('password_strength_strong', 'Strong') };
    return { strength: 0, text: "" };
  };

  const handleNewPasswordChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const pass = e.target.value;
    setNewPassword(pass);
    const { strength, text } = checkPasswordStrength(pass);
    setPasswordStrength(strength);
    setPasswordStrengthText(text);
    setPasswordSuccess("");
    if (passwordErrors.newPassword) setPasswordErrors(prev => ({ ...prev, newPassword: undefined }));
    if (passwordErrors.confirmNewPassword && pass === confirmNewPassword) setPasswordErrors(prev => ({ ...prev, confirmNewPassword: undefined }));
    if (passwordErrors.form) setPasswordErrors(prev => ({...prev, form: undefined}));
  };

  const handleCurrentPasswordChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setCurrentPassword(e.target.value);
    if (passwordErrors.currentPassword) setPasswordErrors(prev => ({ ...prev, currentPassword: undefined }));
    if (passwordErrors.form) setPasswordErrors(prev => ({...prev, form: undefined}));
    setPasswordSuccess("");
  };

  const handleConfirmNewPasswordChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setConfirmNewPassword(e.target.value);
    if (passwordErrors.confirmNewPassword) setPasswordErrors(prev => ({ ...prev, confirmNewPassword: undefined }));
    if (passwordErrors.form) setPasswordErrors(prev => ({...prev, form: undefined}));
    setPasswordSuccess("");
  };

  const validatePasswordChange = (): boolean => {
    const errors: Partial<Record<string, string>> = {};
    if (!currentPassword) errors.currentPassword = t('field_required_error', { fieldName: t('current_password_label','Current Password')});
    if (!newPassword) {
        errors.newPassword = t('field_required_error', { fieldName: t('new_password_label', 'New Password')});
    } else {
        if (newPassword.length < 8) errors.newPassword = t('password_security_rules_error', 'Password must be at least 8 characters.');
        else if (!/[A-Z]/.test(newPassword)) errors.newPassword = t('password_security_rules_error', 'Password must include an uppercase letter.');
        else if (!/[a-z]/.test(newPassword)) errors.newPassword = t('password_security_rules_error', 'Password must include a lowercase letter.');
        else if (!/[0-9]/.test(newPassword)) errors.newPassword = t('password_security_rules_error', 'Password must include a number.');
        else if (!/[^A-Za-z0-9]/.test(newPassword)) errors.newPassword = t('password_security_rules_error', 'Password must include a special character.');
    }
    if (!confirmNewPassword) errors.confirmNewPassword = t('field_required_error', { fieldName: t('confirm_new_password_label', 'Confirm New Password')});
    else if (newPassword && confirmNewPassword !== newPassword) errors.confirmNewPassword = t('passwords_do_not_match_error', 'Passwords do not match.');

    setPasswordErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handlePasswordChangeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setPasswordSuccess("");
    setPasswordErrors({});
    if (!validatePasswordChange()) return;

    try {
      await changePassword({
        currentPassword,
        newPassword,
        confirmPassword: confirmNewPassword,
      });

      setPasswordSuccess(t('password_changed_success', 'Password changed successfully!'));
      setCurrentPassword("");
      setNewPassword("");
      setConfirmNewPassword("");
      setPasswordStrength(0);
      setPasswordStrengthText("");
    } catch (error: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to change password:', error);
      setPasswordErrors({ form: error.message || t('password_change_error', 'Failed to change password. Please try again.') });
    }
  };

  const getStrengthBarStyle = (strengthLevel: number): string => {
    if (passwordStrength === 0) return '';
    if (passwordStrength === 1 && strengthLevel <= 2) return styles.strengthWeak;
    if (passwordStrength === 2 && strengthLevel <= 4) return styles.strengthMedium;
    if (passwordStrength === 3 && strengthLevel <= 5) return styles.strengthStrong;
    if (strengthLevel <=2 && passwordStrength >=1) return styles.strengthWeak;
    if (strengthLevel <=4 && passwordStrength >=2) return styles.strengthMedium;
    return '';
  };

  const [orderHistory, setOrderHistory] = useState<OrderHistoryItem[]>([]);
  const [ordersLoading, setOrdersLoading] = useState(true);

  // Load order history
  useEffect(() => {
    const loadOrders = async () => {
      try {
        const result = await getMyOrders({ pageSize: 50, page: 1 });

        // Transform OrderDto to OrderHistoryItem
        const transformedOrders: OrderHistoryItem[] = result.items.map((order: OrderDto) => ({
          id: order.id,
          date: new Date(order.orderDate).toISOString().split('T')[0],
          status: order.status as OrderStatus,
          totalAmount: order.total,
          orderType: order.type,
          items: order.items.map(item => ({
            id: item.id,
            name: item.productName || item.menuName || 'Unknown Item',
            quantity: item.quantity,
            price: item.unitPrice,
          })),
          deliveryAddress: order.deliveryAddress ? {
            addressLine1: order.deliveryAddress.addressLine1,
            addressLine2: order.deliveryAddress.addressLine2,
            city: order.deliveryAddress.city,
            state: order.deliveryAddress.state,
            postalCode: order.deliveryAddress.postalCode,
            country: order.deliveryAddress.country,
          } : undefined,
        }));

        setOrderHistory(transformedOrders);
      } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to load orders:', error);
        // Fall back to empty array on error
        setOrderHistory([]);
      } finally {
        setOrdersLoading(false);
      }
    };
    loadOrders();
  }, []);

  const getOrderStatusTranslationKey = (status: OrderStatus): string => {
    switch (status) {
      case "Delivered": return 'order_status_delivered';
      case "In Progress": return 'order_status_in_progress';
      case "Cancelled": return 'order_status_cancelled';
      case "Pending Payment": return 'order_status_pending_payment';
      default: return status;
    }
  };

  const [fidelityPoints, setFidelityPoints] = useState<number>(0);
  const pointsForNextReward = 500;

  useEffect(() => {
    // TODO: Load fidelity points from API when endpoint is available
    setFidelityPoints(200);
  }, []);

  return (
    <main className={styles.container}>
      <h1 className={styles.pageTitle}>{t('account_page_title', 'My Account')}</h1>

      <div className={styles.contentGrid}>
        {/* Left Column: Profile, Addresses, and Security */}
        <div className={styles.leftColumn}>
          <PersonalInfoSection
            profile={profile}
            profileErrors={profileErrors}
            profileSuccess={profileSuccess}
            handleProfileChange={handleProfileChange}
            handleProfileSave={handleProfileSave}
          />

          <AddressManagement />

          <PasswordManagementSection
            currentPassword={currentPassword}
            newPassword={newPassword}
            confirmNewPassword={confirmNewPassword}
            passwordErrors={passwordErrors}
            passwordSuccess={passwordSuccess}
            passwordStrength={passwordStrength}
            passwordStrengthText={passwordStrengthText}
            handleCurrentPasswordChange={handleCurrentPasswordChange}
            handleNewPasswordChange={handleNewPasswordChange}
            handleConfirmNewPasswordChange={handleConfirmNewPasswordChange}
            handlePasswordChangeSubmit={handlePasswordChangeSubmit}
            getStrengthBarStyle={getStrengthBarStyle}
          />
        </div>

        {/* Right Column: Orders and Fidelity Points */}
        <div className={styles.rightColumn}>
          <OrderHistorySection
            orderHistory={ordersLoading ? [] : orderHistory}
            getOrderStatusTranslationKey={getOrderStatusTranslationKey}
          />

          <FidelityPointsSection
            fidelityPoints={fidelityPoints}
            pointsForNextReward={pointsForNextReward}
          />
        </div>
      </div>
    </main>
  );
}
