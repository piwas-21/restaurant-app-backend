'use client';

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Save, AlertCircle, CheckCircle2 } from 'lucide-react';
import { workingHoursService } from '@/services/workingHoursService';
import { WorkingHoursDto, UpdateWorkingHoursDto, dayNameToNumber } from '@/types/workingHours';
import { enqueueSnackbar } from 'notistack';
import styles from './WorkingHoursManager.module.css';

const DAYS_ORDER = [1, 2, 3, 4, 5, 6, 0]; // Monday to Sunday

// Normalized type with dayOfWeek as number
type NormalizedWorkingHours = Omit<WorkingHoursDto, 'dayOfWeek'> & { dayOfWeek: number };

export default function WorkingHoursManager() {
  const { t } = useTranslation();
  const [workingHours, setWorkingHours] = useState<NormalizedWorkingHours[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [isOpen, setIsOpen] = useState<boolean | null>(null);

  useEffect(() => {
    loadWorkingHours();
    checkIsOpen();
  }, []);

  const loadWorkingHours = async () => {
    try {
      setLoading(true);
      const hours = await workingHoursService.getAll();

      // Convert dayOfWeek from string to number and sort by day of week (Monday first)
      const normalized: NormalizedWorkingHours[] = hours.map((wh) => ({
        ...wh,
        dayOfWeek: dayNameToNumber(wh.dayOfWeek),
      }));

      const sorted = normalized.sort((a, b) => {
        const aIndex = DAYS_ORDER.indexOf(a.dayOfWeek);
        const bIndex = DAYS_ORDER.indexOf(b.dayOfWeek);
        return aIndex - bIndex;
      });

      setWorkingHours(sorted);
    } catch {
      enqueueSnackbar(t('failed_to_load_working_hours', 'Failed to load working hours'), {
        variant: 'error',
      });
    } finally {
      setLoading(false);
    }
  };

  const checkIsOpen = async () => {
    try {
      const open = await workingHoursService.isOpenNow();
      setIsOpen(open);
    } catch {
      // Silently fail
    }
  };

  const getDayName = (dayOfWeek: number): string => {
    const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const translatedDays = [
      t('sunday', dayNames[0]),
      t('monday', dayNames[1]),
      t('tuesday', dayNames[2]),
      t('wednesday', dayNames[3]),
      t('thursday', dayNames[4]),
      t('friday', dayNames[5]),
      t('saturday', dayNames[6]),
    ];
    return translatedDays[dayOfWeek] || dayNames[dayOfWeek] || 'Unknown';
  };

  const handleTimeChange = (id: string, field: 'openTime' | 'closeTime', value: string) => {
    setWorkingHours((prev) => prev.map((wh) => (wh.id === id ? { ...wh, [field]: value } : wh)));
  };

  const handleToggleClosed = (id: string) => {
    setWorkingHours((prev) => prev.map((wh) => (wh.id === id ? { ...wh, isClosed: !wh.isClosed } : wh)));
  };

  const handleNotesChange = (id: string, notes: string) => {
    setWorkingHours((prev) => prev.map((wh) => (wh.id === id ? { ...wh, notes } : wh)));
  };

  const validateTimes = (): boolean => {
    for (const wh of workingHours) {
      if (!wh.isClosed) {
        const open = parseTime(wh.openTime);
        const close = parseTime(wh.closeTime);
        if (close <= open) {
          enqueueSnackbar(
            t('close_time_must_be_after_open', 'Close time must be after open time for {{day}}', {
              day: getDayName(wh.dayOfWeek),
            }),
            { variant: 'error' },
          );
          return false;
        }
      }
    }
    return true;
  };

  const parseTime = (timeStr: string): number => {
    const [hours, minutes] = timeStr.split(':').map(Number);
    return hours * 60 + minutes;
  };

  const handleSave = async () => {
    if (!validateTimes()) return;

    try {
      setSaving(true);

      // Update all days
      const updatePromises = workingHours.map((wh) => {
        const dto: UpdateWorkingHoursDto = {
          dayOfWeek: wh.dayOfWeek,
          openTime: wh.openTime,
          closeTime: wh.closeTime,
          isActive: wh.isActive,
          isClosed: wh.isClosed,
          notes: wh.notes || null,
        };
        return workingHoursService.update(dto);
      });

      await Promise.all(updatePromises);

      enqueueSnackbar(t('working_hours_updated', 'Working hours updated successfully'), {
        variant: 'success',
      });

      // Reload to get fresh data and check if open status changed
      await loadWorkingHours();
      await checkIsOpen();
    } catch {
      enqueueSnackbar(t('failed_to_update_working_hours', 'Failed to update working hours'), {
        variant: 'error',
      });
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className={styles.loading}>
        <p>{t('common.loading', 'Loading...')}</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Current Status */}
      {isOpen !== null && (
        <div className={`${styles.statusBanner} ${isOpen ? styles.statusOpen : styles.statusClosed}`}>
          {isOpen ? (
            <>
              <CheckCircle2 size={20} />
              <span>{t('restaurant_currently_open', 'Restaurant is currently OPEN')}</span>
            </>
          ) : (
            <>
              <AlertCircle size={20} />
              <span>{t('restaurant_currently_closed', 'Restaurant is currently CLOSED')}</span>
            </>
          )}
        </div>
      )}

      {/* Working Hours Table */}
      <div className={styles.tableWrapper}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th>{t('day', 'Day')}</th>
              <th>{t('status', 'Status')}</th>
              <th>{t('open_time', 'Open Time')}</th>
              <th>{t('close_time', 'Close Time')}</th>
              <th>{t('notes', 'Notes')}</th>
            </tr>
          </thead>
          <tbody>
            {workingHours.map((wh) => (
              <tr key={wh.id} className={wh.isClosed ? styles.closedRow : ''}>
                <td className={styles.dayCell}>
                  <strong suppressHydrationWarning>{getDayName(wh.dayOfWeek)}</strong>
                </td>
                <td>
                  <button
                    type="button"
                    onClick={() => handleToggleClosed(wh.id)}
                    className={`${styles.statusChip} ${wh.isClosed ? styles.statusClosed : styles.statusOpen}`}
                  >
                    {wh.isClosed ? t('closed', 'Closed') : t('open', 'Open')}
                  </button>
                </td>
                <td>
                  <input
                    type="time"
                    value={wh.openTime.substring(0, 5)} // HH:mm
                    onChange={(e) => handleTimeChange(wh.id, 'openTime', e.target.value + ':00')}
                    disabled={wh.isClosed}
                    className={styles.timeInput}
                  />
                </td>
                <td>
                  <input
                    type="time"
                    value={wh.closeTime.substring(0, 5)} // HH:mm
                    onChange={(e) => handleTimeChange(wh.id, 'closeTime', e.target.value + ':00')}
                    disabled={wh.isClosed}
                    className={styles.timeInput}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={wh.notes || ''}
                    onChange={(e) => handleNotesChange(wh.id, e.target.value)}
                    placeholder={t('optional_notes', 'Optional notes...')}
                    className={styles.notesInput}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Save Button */}
      <div className={styles.actions}>
        <button onClick={handleSave} disabled={saving} className={styles.saveButton}>
          <Save size={18} />
          <span>{saving ? t('saving', 'Saving...') : t('save_changes', 'Save Changes')}</span>
        </button>
      </div>
    </div>
  );
}
