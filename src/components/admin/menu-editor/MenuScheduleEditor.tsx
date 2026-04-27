'use client';

import React, { useState, useEffect } from 'react';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';

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

const MenuScheduleEditor: React.FC<MenuScheduleEditorProps> = ({ menuDefinition, onChange }) => {
  const { t } = useTranslation();
  const [localMenuDefinition, setLocalMenuDefinition] = useState<MenuDefinition>(menuDefinition);
  const [hasChanges, setHasChanges] = useState(false);

  // Reset local state when menuDefinition prop changes
  useEffect(() => {
    setLocalMenuDefinition(menuDefinition);
    setHasChanges(false);
  }, [menuDefinition]);

  const _handleToggleAlwaysAvailable = () => {
    setLocalMenuDefinition({
      ...localMenuDefinition,
      isAlwaysAvailable: !localMenuDefinition.isAlwaysAvailable,
    });
    setHasChanges(true);
  };

  const handleTimeChange = (field: 'startTime' | 'endTime', value: string) => {
    setLocalMenuDefinition({
      ...localMenuDefinition,
      [field]: value,
    });
    setHasChanges(true);
  };

  const handleDayToggle = (dayKey: string) => {
    setLocalMenuDefinition({
      ...localMenuDefinition,
      [dayKey]: !localMenuDefinition[dayKey as keyof MenuDefinition],
    });
    setHasChanges(true);
  };

  const handleSave = () => {
    onChange(localMenuDefinition);
    setHasChanges(false);
  };

  const handleCancel = () => {
    setLocalMenuDefinition(menuDefinition);
    setHasChanges(false);
  };

  return (
    <div className={styles.scheduleEditor}>
      <h3 className={styles.sectionTitle}>{t('menu_availability_schedule')}</h3>

      {/* Availability Mode Selection */}
      <div className={modalStyles.formGroup}>
        <label>{t('availability_mode')}</label>
        <div className={styles.chipGroup}>
          <div className={styles.chip}>
            <input
              type="radio"
              id="always-available"
              name="availability-mode"
              checked={localMenuDefinition.isAlwaysAvailable}
              onChange={() => {}}
            />
            <label
              onClick={() => {
                setLocalMenuDefinition({ ...localMenuDefinition, isAlwaysAvailable: true });
                setHasChanges(true);
              }}
              style={{ cursor: 'pointer' }}
            >
              {t('always_available')}
            </label>
          </div>
          <div className={styles.chip}>
            <input
              type="radio"
              id="custom-schedule"
              name="availability-mode"
              checked={!localMenuDefinition.isAlwaysAvailable}
              onChange={() => {}}
            />
            <label
              onClick={() => {
                setLocalMenuDefinition({ ...localMenuDefinition, isAlwaysAvailable: false });
                setHasChanges(true);
              }}
              style={{ cursor: 'pointer' }}
            >
              {t('custom_schedule')}
            </label>
          </div>
        </div>
        <p className={styles.helpText}>
          {localMenuDefinition.isAlwaysAvailable
            ? t('always_available_help')
            : t('custom_schedule_help', { defaultValue: 'Set specific days and times when this menu is available' })}
        </p>
      </div>

      {/* Time Range */}
      {!localMenuDefinition.isAlwaysAvailable && (
        <>
          <div className={styles.timeRange}>
            <div className={modalStyles.formGroup}>
              <label htmlFor="startTime">{t('start_time')}</label>
              <input
                id="startTime"
                type="time"
                value={localMenuDefinition.startTime || ''}
                onChange={(e) => handleTimeChange('startTime', e.target.value)}
                className={styles.timeInput}
              />
            </div>

            <div className={modalStyles.formGroup}>
              <label htmlFor="endTime">{t('end_time')}</label>
              <input
                id="endTime"
                type="time"
                value={localMenuDefinition.endTime || ''}
                onChange={(e) => handleTimeChange('endTime', e.target.value)}
                className={styles.timeInput}
              />
            </div>
          </div>

          {/* Days of Week */}
          <div className={modalStyles.formGroup}>
            <label>{t('available_days')}</label>
            <div className={modalStyles.chipGroup}>
              {DAYS_OF_WEEK.map(({ key, label }) => (
                <div key={key} className={modalStyles.chip}>
                  <input
                    type="checkbox"
                    id={`day-${key}`}
                    checked={localMenuDefinition[key as keyof MenuDefinition] as boolean}
                    onChange={() => handleDayToggle(key)}
                  />
                  <label htmlFor={`day-${key}`}>{t(label)}</label>
                </div>
              ))}
            </div>
          </div>
        </>
      )}

      {/* Save/Cancel Buttons */}
      {hasChanges && (
        <div className={styles.editorActions}>
          <button onClick={handleCancel} className={styles.cancelButton}>
            {t('cancel')}
          </button>
          <button onClick={handleSave} className={styles.saveButton}>
            {t('save')}
          </button>
        </div>
      )}
    </div>
  );
};

export default MenuScheduleEditor;
