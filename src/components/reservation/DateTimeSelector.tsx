import { useTranslation } from 'react-i18next';
import { useState, useEffect } from 'react';
import styles from './DateTimeSelector.module.css';

interface DateTimeSelectorProps {
  selectedDate: string;
  selectedTime: string;
  onDateChange: (date: string) => void;
  onTimeChange: (time: string) => void;
  loading?: boolean;
  availableTimeSlots?: string[];
}

export default function DateTimeSelector({
  selectedDate,
  selectedTime,
  onDateChange,
  onTimeChange,
  loading = false,
  availableTimeSlots
}: DateTimeSelectorProps) {
  const { t, i18n } = useTranslation();
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  // Generate date options (next 14 days)
  const dateOptions = Array.from({ length: 14 }, (_, i) => {
    const date = new Date();
    date.setDate(date.getDate() + i);
    return date;
  });

  // Use provided time slots or fallback (though fallback shouldn't be needed with proper logic)
  const timeSlots = availableTimeSlots || [
    '11:00', '12:00', '13:00', '14:00',
    '17:00', '18:00', '19:00', '20:00', '21:00'
  ];

  return (
    <>
      {/* Date Selection */}
      <div className={styles.formSection}>
        <label className={styles.label}>{t('date', 'Date')}</label>
        <div className={styles.dateSelector}>
          {mounted && dateOptions.map(date => {
            const dateStr = date.toISOString().split('T')[0];
            const dayOfWeek = date.toLocaleDateString(i18n.language, { weekday: 'short' });
            const dayOfMonth = date.getDate();

            return (
              <button
                key={dateStr}
                type="button"
                className={`${styles.dateButton} ${selectedDate === dateStr ? styles.selected : ''}`}
                onClick={() => onDateChange(dateStr)}
              >
                <div className={styles.dateDay}>{dayOfMonth}</div>
                <div className={styles.dateDayName}>{dayOfWeek}</div>
              </button>
            );
          })}
        </div>
        <div className={styles.customInputWrapper}>
          <label className={styles.customLabel}>{t('or_pick_date', 'Or pick a date')}:</label>
          <input
            type="date"
            value={selectedDate}
            onChange={(e) => onDateChange(e.target.value)}
            className={styles.customInput}
            min={mounted ? new Date().toISOString().split('T')[0] : ''}
          />
        </div>
      </div>

      {/* Time Selection */}
      <div className={styles.formSection}>
        <label className={styles.label}>{t('time', 'Time')}</label>
        <div className={styles.timeSelector}>
          {timeSlots.map(time => (
            <button
              key={time}
              type="button"
              className={`${styles.timeButton} ${selectedTime === time ? styles.selected : ''}`}
              onClick={() => onTimeChange(time)}
              disabled={loading}
            >
              {time}
            </button>
          ))}
        </div>
        <div className={styles.customInputWrapper}>
          <label className={styles.customLabel}>{t('or_select_time', 'Or select time')}:</label>
          <select
            value={selectedTime}
            onChange={(e) => onTimeChange(e.target.value)}
            className={styles.customInput}
            disabled={loading || !availableTimeSlots || availableTimeSlots.length === 0}
          >
            <option value="">{t('select_time', 'Select a time...')}</option>
            {timeSlots.map(time => (
              <option key={time} value={time}>
                {time}
              </option>
            ))}
          </select>
        </div>
      </div>
    </>
  );
}
