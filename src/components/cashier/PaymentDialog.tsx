'use client';

import { useState, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto, PaymentMethod } from '@/types/order';
import { X } from 'lucide-react';

interface PaymentDialogProps {
  order: OrderDto | null;
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (paymentData: PaymentDialogData) => Promise<void>;
  isLoading?: boolean;
}

export interface PaymentDialogData {
  amount: number;
  paymentMethod: string;
  transactionId?: string;
  paymentNotes?: string;
}

/**
 * Dialog for adding payment to an order
 * Shows remaining balance and payment method options
 */
export default function PaymentDialog({ order, isOpen, onClose, onConfirm, isLoading = false }: PaymentDialogProps) {
  const { t } = useTranslation();
  const [amount, setAmount] = useState<string>('');
  const [method, setMethod] = useState<string>(PaymentMethod.Cash);
  const [transactionId, setTransactionId] = useState<string>('');
  const [notes, setNotes] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  // Prefill transaction ID with order number when dialog opens
  useEffect(() => {
    if (isOpen && order) {
      setTransactionId(order.orderNumber || order.id || '');
    }
  }, [isOpen, order]);

  // Calculate remaining balance
  const remainingBalance = order?.remainingAmount || 0;

  const handleAmountChange = useCallback((value: string) => {
    // Allow only numbers and decimal point
    const numValue = parseFloat(value);
    if (!isNaN(numValue) || value === '') {
      setAmount(value);
      setError(null);
    }
  }, []);

  const handleSetMaxAmount = useCallback(() => {
    setAmount(remainingBalance.toFixed(2));
    setError(null);
  }, [remainingBalance]);

  const handleConfirm = useCallback(async () => {
    // Validation
    if (!amount || parseFloat(amount) <= 0) {
      setError(t('cashier.payment_amount_required') || 'Please enter a valid payment amount');
      return;
    }

    const paymentAmount = parseFloat(amount);
    if (paymentAmount > remainingBalance) {
      setError(
        t('cashier.payment_exceeds_balance') ||
          `Payment amount cannot exceed remaining balance of ${remainingBalance.toFixed(2)}`,
      );
      return;
    }

    if (!method) {
      setError(t('cashier.payment_method_required') || 'Please select a payment method');
      return;
    }

    try {
      await onConfirm({
        amount: paymentAmount,
        paymentMethod: method,
        transactionId: transactionId || undefined,
        paymentNotes: notes || undefined,
      });

      // Reset form
      setAmount('');
      setMethod(PaymentMethod.Cash);
      setTransactionId('');
      setNotes('');
      onClose();
    } catch (err) {
      setError((err as Error).message || t('cashier.payment_failed') || 'Failed to add payment');
    }
  }, [amount, method, transactionId, notes, remainingBalance, onConfirm, onClose, t]);

  if (!isOpen || !order) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{t('cashier.add_payment') || 'Add Payment'}</h2>
          <button className="modal-close" onClick={onClose} aria-label="Close">
            <X size={20} />
          </button>
        </div>

        <div className="modal-body">
          {/* Order Summary */}
          <div className="form-group">
            <label className="form-label">{t('cashier.order_summary') || 'Order Summary'}</label>
            <div className="summary-grid">
              <div className="summary-item">
                <span className="summary-label">{t('cashier.total') || 'Total'}</span>
                <span className="summary-value">{(order.total || 0).toFixed(2)}</span>
              </div>
              <div className="summary-item">
                <span className="summary-label">{t('cashier.total_paid') || 'Total Paid'}</span>
                <span className="summary-value">{(order.totalPaid || 0).toFixed(2)}</span>
              </div>
              <div className="summary-item">
                <span className="summary-label">{t('cashier.remaining') || 'Remaining'}</span>
                <span className={`summary-value ${remainingBalance > 0 ? 'text-warning' : 'text-success'}`}>
                  {remainingBalance.toFixed(2)}
                </span>
              </div>
            </div>
          </div>

          {/* Payment Amount */}
          <div className="form-group">
            <label className="form-label">{t('cashier.payment_amount') || 'Payment Amount'} *</label>
            <div className="amount-input-group">
              <input
                type="number"
                className="form-input"
                placeholder="0.00"
                value={amount}
                onChange={(e) => handleAmountChange(e.target.value)}
                disabled={isLoading}
                min="0"
                step="0.01"
                max={remainingBalance}
              />
              <button
                className="btn btn-secondary btn-sm"
                onClick={handleSetMaxAmount}
                disabled={isLoading}
                title={t('cashier.use_remaining') || 'Use remaining balance'}
              >
                {t('cashier.max') || 'Max'}
              </button>
            </div>
          </div>

          {/* Payment Method */}
          <div className="form-group">
            <label className="form-label">{t('cashier.payment_method') || 'Payment Method'} *</label>
            <select
              className="form-select"
              value={method}
              onChange={(e) => setMethod(e.target.value)}
              disabled={isLoading}
            >
              <option value={PaymentMethod.Cash}>💵 Cash</option>
              <option value={PaymentMethod.CreditCard}>💳 Credit Card</option>
              <option value={PaymentMethod.DebitCard}>💳 Debit Card</option>
              <option value={PaymentMethod.BankTransfer}>🏦 Bank Transfer</option>
              <option value={PaymentMethod.MobilePayment}>📱 Mobile Payment</option>
              <option value={PaymentMethod.OnlinePayment}>🌐 Online Payment</option>
            </select>
          </div>

          {/* Transaction ID */}
          <div className="form-group">
            <label className="form-label">{t('cashier.transaction_id') || 'Transaction ID (optional)'}</label>
            <input
              type="text"
              className="form-input"
              placeholder={t('cashier.transaction_id_placeholder') || 'Enter transaction ID or reference'}
              value={transactionId}
              onChange={(e) => setTransactionId(e.target.value)}
              disabled={isLoading}
            />
          </div>

          {/* Notes */}
          <div className="form-group">
            <label className="form-label">{t('cashier.notes') || 'Notes (optional)'}</label>
            <textarea
              className="form-textarea"
              placeholder={t('cashier.payment_notes_placeholder') || 'Any additional notes about this payment'}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              disabled={isLoading}
              rows={3}
            />
          </div>

          {error && <div className="alert alert-error">{error}</div>}
        </div>

        <div className="modal-footer">
          <button className="btn btn-secondary" onClick={onClose} disabled={isLoading}>
            {t('common.cancel') || 'Cancel'}
          </button>
          <button className="btn btn-primary" onClick={handleConfirm} disabled={!amount || !method || isLoading}>
            {isLoading ? t('common.loading') || 'Loading...' : t('cashier.add_payment') || 'Add Payment'}
          </button>
        </div>
      </div>
    </div>
  );
}
