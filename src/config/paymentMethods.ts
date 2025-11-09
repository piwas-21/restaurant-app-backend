/**
 * Payment Methods Configuration
 *
 * Defines available payment methods and their properties
 */

import {
  CreditCard,
  Wallet,
  Smartphone,
  Banknote,
  Building2,
} from 'lucide-react';
import { PaymentMethod } from '@/types/order';
import type { LucideIcon } from 'lucide-react';

export interface PaymentMethodOption {
  value: PaymentMethod;
  labelKey: string;
  label: string;
  icon: LucideIcon;
  descriptionKey: string;
  description: string;
  disabled: boolean;
}

export const PAYMENT_METHODS: PaymentMethodOption[] = [
  {
    value: PaymentMethod.Cash,
    labelKey: 'payment_cash',
    label: 'Cash',
    icon: Banknote,
    descriptionKey: 'payment_cash_desc',
    description: 'Pay on cashier',
    disabled: false
  },
  {
    value: PaymentMethod.CreditCard,
    labelKey: 'payment_credit_card',
    label: 'Credit Card',
    icon: CreditCard,
    descriptionKey: 'payment_credit_card_desc',
    description: 'Visa, Mastercard, Amex',
    disabled: true
  },
  {
    value: PaymentMethod.DebitCard,
    labelKey: 'payment_debit_card',
    label: 'Debit Card',
    icon: Wallet,
    descriptionKey: 'payment_debit_card_desc',
    description: 'EC/Maestro card',
    disabled: true
  },
  {
    value: PaymentMethod.MobilePayment,
    labelKey: 'payment_mobile',
    label: 'Mobile Payment',
    icon: Smartphone,
    descriptionKey: 'payment_mobile_desc',
    description: 'TWINT, Apple Pay, Google Pay',
    disabled: true
  },
  {
    value: PaymentMethod.OnlinePayment,
    labelKey: 'payment_online',
    label: 'Online Payment',
    icon: CreditCard,
    descriptionKey: 'payment_online_desc',
    description: 'Pay securely online',
    disabled: true
  },
  {
    value: PaymentMethod.BankTransfer,
    labelKey: 'payment_bank_transfer',
    label: 'Bank Transfer',
    icon: Building2,
    descriptionKey: 'payment_bank_transfer_desc',
    description: 'Transfer to our account',
    disabled: true
  },
];
