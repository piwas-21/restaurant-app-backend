'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calendar } from 'lucide-react';
import styles from './DateRangeFilter.module.css';

export type DateRangePreset = 'today' | 'yesterday' | 'last7days' | 'last30days' | 'custom';

interface DateRangeFilterProps {
  onDateRangeChange: (startDate: string | null, endDate: string | null, preset: DateRangePreset) => void;
}

export default function DateRangeFilter({ onDateRangeChange }: DateRangeFilterProps) {
  const { t } = useTranslation();
  const [selectedPreset, setSelectedPreset] = useState<DateRangePreset | 'all'>('all');
  const [customStartDate, setCustomStartDate] = useState('');
  const [customEndDate, setCustomEndDate] = useState('');
  const [showCustomPicker, setShowCustomPicker] = useState(false);

  const getDateRange = (preset: DateRangePreset): { startDate: string; endDate: string } => {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    switch (preset) {
      case 'today': {
        const start = today.toISOString();
        const end = new Date(today.getTime() + 24 * 60 * 60 * 1000 - 1).toISOString();
        return { startDate: start, endDate: end };
      }
      case 'yesterday': {
        const yesterday = new Date(today.getTime() - 24 * 60 * 60 * 1000);
        const start = yesterday.toISOString();
        const end = new Date(yesterday.getTime() + 24 * 60 * 60 * 1000 - 1).toISOString();
        return { startDate: start, endDate: end };
      }
      case 'last7days': {
        const sevenDaysAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
        return { startDate: sevenDaysAgo.toISOString(), endDate: now.toISOString() };
      }
      case 'last30days': {
        const thirtyDaysAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
        return { startDate: thirtyDaysAgo.toISOString(), endDate: now.toISOString() };
      }
      case 'custom':
        return { startDate: customStartDate, endDate: customEndDate };
    }
  };

  const handlePresetChange = (preset: DateRangePreset | 'all') => {
    setSelectedPreset(preset);
    setShowCustomPicker(preset === 'custom');

    if (preset === 'all') {
      onDateRangeChange(null, null, 'today');
    } else if (preset === 'custom') {
      if (customStartDate && customEndDate) {
        onDateRangeChange(customStartDate, customEndDate, 'custom');
      }
    } else {
      const { startDate, endDate } = getDateRange(preset);
      onDateRangeChange(startDate, endDate, preset);
    }
  };

  const handleCustomDateApply = () => {
    if (customStartDate && customEndDate) {
      onDateRangeChange(customStartDate, customEndDate, 'custom');
    }
  };

  return (
    <div className={styles.container}>
      <div className={styles.label}>
        <Calendar size={16} />
        <span>{t('date_range', 'Date Range')}</span>
      </div>
      <div className={styles.presets}>
        <button
          onClick={() => handlePresetChange('all')}
          className={`${styles.presetButton} ${selectedPreset === 'all' ? styles.active : ''}`}
        >
          {t('all_time', 'All Time')}
        </button>
        <button
          onClick={() => handlePresetChange('today')}
          className={`${styles.presetButton} ${selectedPreset === 'today' ? styles.active : ''}`}
        >
          {t('today', 'Today')}
        </button>
        <button
          onClick={() => handlePresetChange('yesterday')}
          className={`${styles.presetButton} ${selectedPreset === 'yesterday' ? styles.active : ''}`}
        >
          {t('yesterday', 'Yesterday')}
        </button>
        <button
          onClick={() => handlePresetChange('last7days')}
          className={`${styles.presetButton} ${selectedPreset === 'last7days' ? styles.active : ''}`}
        >
          {t('last_7_days', 'Last 7 Days')}
        </button>
        <button
          onClick={() => handlePresetChange('last30days')}
          className={`${styles.presetButton} ${selectedPreset === 'last30days' ? styles.active : ''}`}
        >
          {t('last_30_days', 'Last 30 Days')}
        </button>
        <button
          onClick={() => handlePresetChange('custom')}
          className={`${styles.presetButton} ${selectedPreset === 'custom' ? styles.active : ''}`}
        >
          {t('custom_range', 'Custom Range')}
        </button>
      </div>

      {showCustomPicker && (
        <div className={styles.customPicker}>
          <div className={styles.dateInputGroup}>
            <label htmlFor="startDate">{t('from', 'From')}</label>
            <input
              id="startDate"
              type="date"
              value={customStartDate}
              onChange={(e) => setCustomStartDate(e.target.value)}
              className={styles.dateInput}
            />
          </div>
          <div className={styles.dateInputGroup}>
            <label htmlFor="endDate">{t('to', 'To')}</label>
            <input
              id="endDate"
              type="date"
              value={customEndDate}
              onChange={(e) => setCustomEndDate(e.target.value)}
              className={styles.dateInput}
            />
          </div>
          <button
            onClick={handleCustomDateApply}
            className={styles.applyButton}
            disabled={!customStartDate || !customEndDate}
          >
            {t('apply', 'Apply')}
          </button>
        </div>
      )}
    </div>
  );
}
