'use client';

import React from 'react';
import styles from './MenuEditor.module.css';
import { MenuDefinition } from '@/types/menu';
import { useTranslation } from 'react-i18next';

interface MenuScheduleEditorProps {
  menuDefinition: MenuDefinition;
  onChange: (menuDefinition: MenuDefinition) => void;
}

const DAYS_OF_WEEK = [
  { key: 'availableMonday', label: 'monday' },
  { key: 'availableTuesday', label: 'tuesday' },
  { key: 'availableWednesday', label: 'wednesday' },
  { key: 'availableThursday', label: 'thursday' },
  { key: 'availableFriday', label: 'friday' },
  { key: 'availableSaturday', label: 'saturday' },
  { key: 'availableSunday', label: 'sunday' },
] as const;

const MenuScheduleEditor: React.FC<MenuScheduleEditorProps> = ({
  menuDefinition,
  onChange,
}) => {
  const { t } = useTranslation();

  const handleToggleAlwaysAvailable = () => {
    onChange({
      ...menuDefinition,
      isAlwaysAvailable: !menuDefinition.isAlwaysAvailable,
    });
  };

  const handleTimeChange = (field: 'startTime' | 'endTime', value: string) => {
    onChange({
      ...menuDefinition,
      [field]: value,
    });
  };

  const handleDayToggle = (dayKey: string) => {
    onChange({
      ...menuDefinition,
      [dayKey]: !menuDefinition[dayKey as keyof MenuDefinition],
    });
  };

  return (
    <div className={styles.scheduleEditor}>
      <h3 className={styles.sectionTitle}>{t('menu_availability_schedule')}</h3>

      {/* Always Available Toggle */}
      <div className={styles.formGroup}>
        <label className={styles.toggleLabel}>
          <input
            type="checkbox"
            checked={menuDefinition.isAlwaysAvailable}
            onChange={handleToggleAlwaysAvailable}
            className={styles.checkbox}
          />
          <span>{t('always_available')}</span>
        </label>
        <p className={styles.helpText}>
          {t('always_available_help')}
        </p>
      </div>

      {/* Time Range */}
      {!menuDefinition.isAlwaysAvailable && (
        <>
          <div className={styles.timeRange}>
            <div className={styles.formGroup}>
              <label htmlFor="startTime">{t('start_time')}</label>
              <input
                id="startTime"
                type="time"
                value={menuDefinition.startTime || ''}
                onChange={(e) => handleTimeChange('startTime', e.target.value)}
                className={styles.timeInput}
              />
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="endTime">{t('end_time')}</label>
              <input
                id="endTime"
                type="time"
                value={menuDefinition.endTime || ''}
                onChange={(e) => handleTimeChange('endTime', e.target.value)}
                className={styles.timeInput}
              />
            </div>
          </div>

          {/* Days of Week */}
          <div className={styles.formGroup}>
            <label>{t('available_days')}</label>
            <div className={styles.daysGrid}>
              {DAYS_OF_WEEK.map(({ key, label }) => (
                <label key={key} className={styles.dayCheckbox}>
                  <input
                    type="checkbox"
                    checked={menuDefinition[key as keyof MenuDefinition] as boolean}
                    onChange={() => handleDayToggle(key)}
                    className={styles.checkbox}
                  />
                  <span>{t(label)}</span>
                </label>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
};

export default MenuScheduleEditor;
