"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import styles from '../styles/AccountPage.module.css';

import PersonalInfoSection from '../../components/account/PersonalInfoSection';
import PasswordManagementSection from '../../components/account/PasswordManagementSection';
import OrderHistorySection from '../../components/account/OrderHistorySection';
import FidelityPointsSection from '../../components/account/FidelityPointsSection';

export interface UserProfile {
  fullName: string;
  email: string;
  phoneNumber: string;
  deliveryAddress: {
    street: string;
    city: string;
    zipCode: string;
    country: string;
  };
}

export type OrderStatus = "Delivered" | "In Progress" | "Cancelled" | "Pending Payment";

// Add detailed items to OrderHistoryItem
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
  items: OrderItemDetail[]; // Array of items in the order
}

export type ProfileErrorKeys =
  | "fullName"
  | "email"
  | "phoneNumber"
  | "deliveryAddress.street"
  | "deliveryAddress.city"
  | "deliveryAddress.zipCode"
  | "deliveryAddress.country"
  | "form";

const mockUserProfile: UserProfile = {
  fullName: "Jane Doe",
  email: "jane.doe@example.com",
  phoneNumber: "+41 79 123 45 67",
  deliveryAddress: {
    street: "Example Street 123",
    city: "Geneva",
    zipCode: "1201",
    country: "Switzerland",
  },
};

const mockOrderHistory: OrderHistoryItem[] = [
  {
    id: "ORD789", date: "2023-10-25", status: "Delivered", totalAmount: 45.50,
    items: [
      { id: "item1", name: "Adana Kebab", quantity: 1, price: 23.90 },
      { id: "item2", name: "Pide - Cheese", quantity: 1, price: 18.00 },
      { id: "item3", name: "Ayran", quantity: 1, price: 3.60 },
    ]
  },
  {
    id: "ORD654", date: "2023-11-10", status: "In Progress", totalAmount: 32.00,
    items: [
      { id: "item4", name: "Dürüm Wrap - Falafel", quantity: 2, price: 16.00 },
    ]
  },
  {
    id: "ORD321", date: "2023-09-15", status: "Cancelled", totalAmount: 19.90,
    items: [
      { id: "item5", name: "Sarma (Vegan)", quantity: 10, price: 1.99 }, // Price corrected for example
    ]
  },
  {
    id: "ORD111", date: "2023-11-20", status: "Pending Payment", totalAmount: 55.00,
    items: [
      { id: "item6", name: "Mixed Grill Platter", quantity: 1, price: 35.00 },
      { id: "item7", name: "Baklava", quantity: 1, price: 9.50 },
      { id: "item8", name: "Turkish Coffee", quantity: 2, price: 5.25 },
    ]
  },
];

const mockFidelityPoints = 1250;
const MOCK_CURRENT_PASSWORD = "password123";

export default function AccountPage() {
  const { t } = useTranslation();

  const [profile, setProfile] = useState<UserProfile>(mockUserProfile);
  const [profileErrors, setProfileErrors] = useState<Partial<Record<ProfileErrorKeys, string>>>({});
  const [profileSuccess, setProfileSuccess] = useState<string>("");

  const handleProfileChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    const { name, value } = e.target;
    const [section, field] = name.split('.');
    if (section === 'deliveryAddress' && field) {
      setProfile(prev => ({ ...prev, deliveryAddress: { ...prev.deliveryAddress, [field]: value } }));
    } else {
      setProfile(prev => ({ ...prev, [name]: value }));
    }
    if (profileErrors[name as ProfileErrorKeys] || profileErrors.form) {
      setProfileErrors(prev => ({ ...prev, [name as ProfileErrorKeys]: undefined, form: undefined }));
    }
    setProfileSuccess("");
  };

  const validateProfile = (): boolean => {
    const errors: Partial<Record<ProfileErrorKeys, string>> = {};
    if (!profile.fullName.trim()) errors.fullName = t('field_required_error', { fieldName: t('full_name_label', 'Full Name') });
    if (!profile.email.trim()) {
      errors.email = t('field_required_error', { fieldName: t('email_label', 'Email') });
    } else if (!/\S+@\S+\.\S+/.test(profile.email)) {
      errors.email = t('invalid_email_error', 'Please enter a valid email address.');
    }
    if (!profile.phoneNumber.trim()) errors.phoneNumber = t('field_required_error', { fieldName: t('customer_phone_label', 'Phone Number') });
    if (!profile.deliveryAddress.street.trim()) errors["deliveryAddress.street"] = t('field_required_error', { fieldName: t('address_street_label', 'Street') });
    if (!profile.deliveryAddress.city.trim()) errors["deliveryAddress.city"] = t('field_required_error', { fieldName: t('address_city_label', 'City') });
    if (!profile.deliveryAddress.zipCode.trim()) errors["deliveryAddress.zipCode"] = t('field_required_error', { fieldName: t('address_zip_label', 'ZIP Code') });
    if (!profile.deliveryAddress.country.trim()) errors["deliveryAddress.country"] = t('field_required_error', { fieldName: t('address_country_label', 'Country') });
    setProfileErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleProfileSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setProfileSuccess("");
    setProfileErrors({});
    if (!validateProfile()) return;
    console.log("Saving profile changes:", profile);
    await new Promise(resolve => setTimeout(resolve, 1000));
    setProfileSuccess(t('changes_saved_success', 'Your information has been updated!'));
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
    if (currentPassword && currentPassword !== MOCK_CURRENT_PASSWORD) {
        errors.currentPassword = t('incorrect_current_password_error', 'Incorrect current password.');
    }
    setPasswordErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handlePasswordChangeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setPasswordSuccess("");
    setPasswordErrors({});
    if (!validatePasswordChange()) return;
    console.log("Changing password. New password:", newPassword);
    await new Promise(resolve => setTimeout(resolve, 1000));
    setPasswordSuccess(t('password_changed_success', 'Password changed successfully!'));
    setCurrentPassword("");
    setNewPassword("");
    setConfirmNewPassword("");
    setPasswordStrength(0);
    setPasswordStrengthText("");
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

  const [orderHistory, setOrderHistory] = useState<OrderHistoryItem[]>(mockOrderHistory);
  const getOrderStatusTranslationKey = (status: OrderStatus): string => {
    console.log('setOrderHistory', setOrderHistory); //to be completed when api is ready
    switch (status) {
      case "Delivered": return 'order_status_delivered';
      case "In Progress": return 'order_status_in_progress';
      case "Cancelled": return 'order_status_cancelled';
      case "Pending Payment": return 'order_status_pending_payment';
      default: return status; 
    }
  };

  const [fidelityPoints, setFidelityPoints] = useState<number>(mockFidelityPoints);
  const pointsForNextReward = 500;

  useEffect(() => {
    setFidelityPoints(200) //to be handled by api
  }, []);

  return (
    <main className={styles.container}>
      <h1 className={styles.pageTitle}>{t('account_page_title', 'My Account')}</h1>

      <PersonalInfoSection 
        profile={profile}
        profileErrors={profileErrors}
        profileSuccess={profileSuccess}
        handleProfileChange={handleProfileChange}
        handleProfileSave={handleProfileSave}
      />

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

      <OrderHistorySection 
        orderHistory={orderHistory}
        getOrderStatusTranslationKey={getOrderStatusTranslationKey}
      />

      <FidelityPointsSection 
        fidelityPoints={fidelityPoints}
        pointsForNextReward={pointsForNextReward} 
      />
    </main>
  );
}
