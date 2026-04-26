import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Heart } from 'lucide-react';
import styles from './TipSelector.module.css';

interface TipSelectorProps {
  subtotal: number;
  selectedTipAmount: number;
  onTipChange: (amount: number) => void;
}

type TipOption = 'none' | 10 | 15 | 20 | 'custom';

export default function TipSelector({
  subtotal,
  selectedTipAmount,
  onTipChange,
}: TipSelectorProps) {
  const { t } = useTranslation();
  const [selectedOption, setSelectedOption] = useState<TipOption>('none');
  const [customAmount, setCustomAmount] = useState('');

  const roundTipAmount = (amount: number) => {
    // Avoid floating point errors by working with integers
    const totalCents = Math.round(amount * 100);
    const centsPart = totalCents % 100;

    // Special rule: everything below .10 goes to .00
    if (centsPart < 10) {
      return (totalCents - centsPart) / 100;
    }

    // Standard rule: round down to nearest 0.05
    const remainder = totalCents % 5;
    return (totalCents - remainder) / 100;
  };

  // Update selected option based on tip amount
  useEffect(() => {
    if (selectedTipAmount === 0) {
      setSelectedOption('none');
      setCustomAmount('');
    } else {
      // Check if it matches a percentage (using rounded values)
      const tip10 = roundTipAmount(subtotal * 0.10);
      const tip15 = roundTipAmount(subtotal * 0.15);
      const tip20 = roundTipAmount(subtotal * 0.20);

      // Use a small epsilon for float comparison
      if (Math.abs(selectedTipAmount - tip10) < 0.001) {
        setSelectedOption(10);
      } else if (Math.abs(selectedTipAmount - tip15) < 0.001) {
        setSelectedOption(15);
      } else if (Math.abs(selectedTipAmount - tip20) < 0.001) {
        setSelectedOption(20);
      } else {
        setSelectedOption('custom');
        setCustomAmount(selectedTipAmount.toFixed(2));
      }
    }
  }, [selectedTipAmount, subtotal]);

  const handleOptionClick = (option: TipOption) => {
    setSelectedOption(option);

    if (option === 'none') {
      onTipChange(0);
      setCustomAmount('');
    } else if (option === 'custom') {
      // Focus will be on input, wait for user to enter amount
      return;
    } else {
      // Calculate percentage tip
      const rawTip = subtotal * (option / 100);
      const roundedTip = roundTipAmount(rawTip);
      onTipChange(roundedTip);
      setCustomAmount('');
    }
  };

  const handleCustomAmountChange = (value: string) => {
    setCustomAmount(value);

    // Parse and validate
    const amount = parseFloat(value);
    if (!isNaN(amount) && amount >= 0) {
      setSelectedOption('custom');
      onTipChange(amount);
    } else if (value === '' || value === '0') {
      onTipChange(0);
    }
  };

  const calculateTipAmount = (percentage: number) => {
    return roundTipAmount(subtotal * (percentage / 100));
  };

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(price);
  };

  return (
    <div className={styles.tipCard}>
      <div className={styles.tipHeader}>
        <div className={styles.tipHeaderContent}>
          <Heart className={styles.heartIcon} size={20} />
          <h3 className={styles.tipTitle}>
            {t('tip', 'Tip')}
          </h3>
        </div>
        <span className={styles.optionalBadge}>
          {t('tip_optional', 'Optional')}
        </span>
      </div>

      <div className={styles.tipOptions}>
        {/* No Tip Option */}
        <button
          type="button"
          className={`${styles.tipButton} ${selectedOption === 'none' ? styles.selected : ''}`}
          onClick={() => handleOptionClick('none')}
          aria-label={t('no_tip', 'No Tip')}
        >
          <span className={styles.tipLabel}>{t('no_tip', 'No Tip')}</span>
        </button>

        {/* 10% Option */}
        <button
          type="button"
          className={`${styles.tipButton} ${selectedOption === 10 ? styles.selected : ''}`}
          onClick={() => handleOptionClick(10)}
          aria-label={`10% ${formatPrice(calculateTipAmount(10))}`}
        >
          <span className={styles.tipPercentage}>10%</span>
          <span className={styles.tipAmount}>{formatPrice(calculateTipAmount(10))}</span>
        </button>

        {/* 15% Option */}
        <button
          type="button"
          className={`${styles.tipButton} ${selectedOption === 15 ? styles.selected : ''}`}
          onClick={() => handleOptionClick(15)}
          aria-label={`15% ${formatPrice(calculateTipAmount(15))}`}
        >
          <span className={styles.tipPercentage}>15%</span>
          <span className={styles.tipAmount}>{formatPrice(calculateTipAmount(15))}</span>
        </button>

        {/* 20% Option */}
        <button
          type="button"
          className={`${styles.tipButton} ${selectedOption === 20 ? styles.selected : ''}`}
          onClick={() => handleOptionClick(20)}
          aria-label={`20% ${formatPrice(calculateTipAmount(20))}`}
        >
          <span className={styles.tipPercentage}>20%</span>
          <span className={styles.tipAmount}>{formatPrice(calculateTipAmount(20))}</span>
        </button>
      </div>

      {/* Custom Tip Input */}
      <div className={styles.customTipContainer}>
        <label htmlFor="custom-tip" className={styles.customTipLabel}>
          {t('custom_tip', 'Custom Amount')}
        </label>
        <div className={styles.customTipInputWrapper}>
          <span className={styles.currencySymbol}>CHF</span>
          <input
            id="custom-tip"
            type="number"
            min="0"
            step="0.50"
            value={customAmount}
            onChange={(e) => handleCustomAmountChange(e.target.value)}
            onFocus={() => setSelectedOption('custom')}
            placeholder="0.00"
            className={styles.customTipInput}
            aria-label={t('enter_custom_tip', 'Enter custom tip amount')}
          />
        </div>
      </div>
    </div>
  );
}
