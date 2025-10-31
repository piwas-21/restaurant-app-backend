'use client';

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { reservationService } from '@/services/reservationService';
import { TableDto } from '@/types/reservation';
import VisualTableLayout from '@/components/reservation/VisualTableLayout';
import styles from './styles.module.css';
import { enqueueSnackbar } from 'notistack';

export default function ReservationsPage() {
  const { t } = useTranslation();

  // State
  const [selectedDate, setSelectedDate] = useState<string>('');
  const [selectedTime, setSelectedTime] = useState<string>('');
  const [selectedTableIds, setSelectedTableIds] = useState<string[]>([]);
  const [requestCombineTables, setRequestCombineTables] = useState<boolean>(false);
  const [numberOfGuests, setNumberOfGuests] = useState<number>(2);
  const [customerName, setCustomerName] = useState<string>('');
  const [customerEmail, setCustomerEmail] = useState<string>('');
  const [customerPhone, setCustomerPhone] = useState<string>('');
  const [specialRequests, setSpecialRequests] = useState<string>('');

  const [allTables, setAllTables] = useState<TableDto[]>([]);
  const [availableTables, setAvailableTables] = useState<TableDto[]>([]);
  const [bookedTableIds, setBookedTableIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [capacityWarning, setCapacityWarning] = useState<string>('');

  // Generate date options (next 14 days)
  const dateOptions = Array.from({ length: 14 }, (_, i) => {
    const date = new Date();
    date.setDate(date.getDate() + i);
    return date;
  });

  // Time slots
  const timeSlots = [
    '11:00', '12:00', '13:00', '14:00',
    '17:00', '18:00', '19:00', '20:00', '21:00', '22:00'
  ];

  // Load all tables on mount
  useEffect(() => {
    loadAllTables();
  }, []);

  // Check availability when date/time/guests change
  useEffect(() => {
    if (selectedDate && selectedTime) {
      checkAvailability();
    }
  }, [selectedDate, selectedTime, numberOfGuests]);

  const loadAllTables = async () => {
    try {
      const tables = await reservationService.getTables(true); // Only active tables
      setAllTables(tables);
      setAvailableTables(tables); // Initially all are available
    } catch (err) {
      console.error('Failed to load tables:', err);
      enqueueSnackbar(t('failed_to_load_tables', 'Failed to load tables'), { variant: 'error' });
    }
  };

  const checkAvailability = async () => {
    if (!selectedDate || !selectedTime) return;

    setLoading(true);
    setCapacityWarning(''); // Clear previous warnings

    try {
      const result = await reservationService.getAvailableTimeSlots(selectedDate, numberOfGuests);

      // Check if there's a capacity issue (expected scenario, not an error)
      if (result.isCapacityIssue && result.error) {
        // Show all available tables with a warning
        setCapacityWarning(result.error);
        setAvailableTables(allTables.filter(t => t.isActive));
        setBookedTableIds([]);
      } else if (result.error) {
        // Other API errors
        console.error('Failed to check availability:', result.error);
        setAvailableTables([]);
        setBookedTableIds(allTables.map(t => t.id));
      } else if (result.data) {
        // Success - find the time slot that matches
        const slot = result.data.timeSlots.find(s => s.startTime.startsWith(selectedTime));

        if (slot) {
          setAvailableTables(slot.availableTables);

          // Calculate booked tables
          const availableIds = new Set(slot.availableTables.map(t => t.id));
          const booked = allTables.filter(t => !availableIds.has(t.id)).map(t => t.id);
          setBookedTableIds(booked);
        } else {
          setAvailableTables([]);
          setBookedTableIds(allTables.map(t => t.id));
        }
      }
    } catch (err: any) {
      // Unexpected network errors
      console.error('Unexpected error checking availability:', err);
      setAvailableTables([]);
      setBookedTableIds(allTables.map(t => t.id));
    } finally {
      setLoading(false);
    }
  };

  const handleTableSelect = (table: TableDto) => {
    setSelectedTableIds(prev => {
      if (prev.includes(table.id)) {
        // Deselect if already selected
        return prev.filter(id => id !== table.id);
      } else {
        // Add to selection
        return [...prev, table.id];
      }
    });
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (selectedTableIds.length === 0 || !selectedDate || !selectedTime) {
      enqueueSnackbar(t('please_complete_all_fields', 'Please complete all fields'), { variant: 'warning' });
      return;
    }

    if (!customerName || !customerEmail) {
      enqueueSnackbar(t('please_fill_customer_details', 'Please fill in your details'), { variant: 'warning' });
      return;
    }

    setSubmitting(true);

    try {
      // Prepare special requests with combine info if needed
      let finalSpecialRequests = specialRequests || '';

      // Add capacity warning note if present
      if (capacityWarning) {
        finalSpecialRequests = `[CAPACITY REVIEW NEEDED: Requested ${numberOfGuests} guests but individual table capacity may be insufficient. Customer selected ${selectedTableIds.length} table(s). Please review and confirm if arrangement can accommodate party size.] ${finalSpecialRequests}`.trim();
      }

      if (requestCombineTables && selectedTableIds.length > 1) {
        const tableNumbers = selectedTableIds
          .map(id => allTables.find(t => t.id === id)?.tableNumber)
          .filter(Boolean)
          .join(', ');
        finalSpecialRequests = `[REQUEST TO COMBINE TABLES: ${tableNumbers}] ${finalSpecialRequests}`.trim();
      }

      // Create reservations for all selected tables
      const reservationPromises = selectedTableIds.map(tableId => {
        const reservationData = {
          customerName,
          customerEmail,
          customerPhone,
          tableId,
          reservationDate: new Date(selectedDate).toISOString(),
          startTime: `${selectedTime}:00`,
          endTime: `${parseInt(selectedTime.split(':')[0]) + 2}:00:00`, // 2-hour reservation
          numberOfGuests,
          specialRequests: finalSpecialRequests || undefined
        };
        return reservationService.createReservation(reservationData);
      });

      await Promise.all(reservationPromises);

      const successMessage = selectedTableIds.length > 1
        ? t('multiple_reservations_success', `Successfully reserved ${selectedTableIds.length} tables!`)
        : t('reservation_success_message', 'Your reservation has been created successfully!');

      enqueueSnackbar(successMessage, { variant: 'success' });

      // Reset form
      setSelectedTableIds([]);
      setRequestCombineTables(false);
      setCustomerName('');
      setCustomerEmail('');
      setCustomerPhone('');
      setSpecialRequests('');

      setTimeout(() => {
        window.location.href = '/';
      }, 2000);
    } catch (err: any) {
      enqueueSnackbar(
        err.message || t('reservation_failed', 'Failed to create reservation'),
        { variant: 'error' }
      );
    } finally {
      setSubmitting(false);
    }
  };

  const selectedTables = allTables.filter(t => selectedTableIds.includes(t.id));
  const canSubmit = selectedTableIds.length > 0 && selectedDate && selectedTime && customerName && customerEmail;

  return (
    <div className={styles.container}>
      <div className={styles.content}>
        <h1 className={styles.title}>{t('make_reservation', 'Make a Reservation')}</h1>

        <div className={styles.layout}>
          {/* Visual Table Layout */}
          <div className={styles.floorPlanSection}>
            <h2 className={styles.sectionTitle}>
              {t('select_your_tables', 'Select your Table(s)')}
              {selectedTableIds.length > 0 && (
                <span style={{ marginLeft: '1rem', color: '#f4c430', fontWeight: 'normal', fontSize: '0.9rem' }}>
                  ({selectedTableIds.length} {selectedTableIds.length === 1 ? t('table_selected', 'table') : t('tables_selected', 'tables')} selected)
                </span>
              )}
            </h2>

            {/* Capacity Warning */}
            {capacityWarning && (
              <div className={styles.capacityWarning}>
                <div className={styles.warningIcon}>⚠️</div>
                <div className={styles.warningContent}>
                  <p className={styles.warningTitle}>
                    {t('capacity_notice', 'Capacity Notice')}
                  </p>
                  <p className={styles.warningMessage}>
                    {t('capacity_warning_message',
                      'We don\'t have a single table that can accommodate all {{guests}} guests. However, you can select multiple tables and request to combine them, or proceed with your selection and our staff will review your request to find the best arrangement.',
                      { guests: numberOfGuests }
                    )}
                  </p>
                  <p className={styles.warningAction}>
                    {t('select_multiple_tables_suggestion', '💡 Tip: Select multiple tables and use the "combine tables" option below.')}
                  </p>
                </div>
              </div>
            )}

            <VisualTableLayout
              tables={allTables}
              selectedTableIds={selectedTableIds}
              onSelectTable={handleTableSelect}
              bookedTableIds={bookedTableIds}
            />
          </div>

          {/* Booking Panel */}
          <div className={styles.bookingPanel}>
            <h2 className={styles.panelTitle}>{t('book_your_table', 'Book your table')}</h2>

            <form onSubmit={handleSubmit} className={styles.bookingForm}>
              {/* Number of Guests */}
              <div className={styles.formSection}>
                <label className={styles.label}>{t('guests', 'Guests')}</label>
                <div className={styles.guestSelector}>
                  {[1, 2, 3, 4, 5, 6, 7, 8].map(num => (
                    <button
                      key={num}
                      type="button"
                      className={`${styles.guestButton} ${numberOfGuests === num ? styles.selected : ''}`}
                      onClick={() => setNumberOfGuests(num)}
                    >
                      {num}
                    </button>
                  ))}
                </div>
                <div className={styles.customInputWrapper}>
                  <label className={styles.customLabel}>{t('or_custom', 'Or custom')}:</label>
                  <input
                    type="number"
                    min="1"
                    max="50"
                    value={numberOfGuests}
                    onChange={(e) => setNumberOfGuests(parseInt(e.target.value) || 1)}
                    className={styles.customInput}
                    placeholder={t('enter_guests', 'Enter number')}
                  />
                </div>
              </div>

              {/* Date Selection */}
              <div className={styles.formSection}>
                <label className={styles.label}>{t('date', 'Date')}</label>
                <div className={styles.dateSelector}>
                  {dateOptions.map(date => {
                    const dateStr = date.toISOString().split('T')[0];
                    const dayOfWeek = date.toLocaleDateString('en-US', { weekday: 'short' });
                    const dayOfMonth = date.getDate();

                    return (
                      <button
                        key={dateStr}
                        type="button"
                        className={`${styles.dateButton} ${selectedDate === dateStr ? styles.selected : ''}`}
                        onClick={() => setSelectedDate(dateStr)}
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
                    onChange={(e) => setSelectedDate(e.target.value)}
                    className={styles.customInput}
                    min={new Date().toISOString().split('T')[0]}
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
                      onClick={() => setSelectedTime(time)}
                      disabled={loading}
                    >
                      {time}
                    </button>
                  ))}
                </div>
                <div className={styles.customInputWrapper}>
                  <label className={styles.customLabel}>{t('or_enter_time', 'Or enter time')}:</label>
                  <input
                    type="time"
                    value={selectedTime}
                    onChange={(e) => setSelectedTime(e.target.value)}
                    className={styles.customInput}
                    min="11:00"
                    max="22:00"
                  />
                </div>
              </div>

              {/* Selected Table Display */}
              {selectedTables.length > 0 && (
                <div className={styles.selectedTableInfo}>
                  <div className={styles.tableLabel}>
                    {selectedTables.length === 1 ? t('table', 'Table') : t('tables', 'Tables')}:
                  </div>
                  <div className={styles.tableValue}>
                    {selectedTables.map(t => t.tableNumber).join(', ')}
                  </div>
                </div>
              )}

              {/* Combine Tables Chip */}
              {selectedTableIds.length > 1 && (
                <div className={styles.formSection}>
                  <button
                    type="button"
                    onClick={() => setRequestCombineTables(!requestCombineTables)}
                    className={`${styles.combineChip} ${requestCombineTables ? styles.combineChipActive : ''}`}
                  >
                    <span className={styles.combineChipIcon}>{requestCombineTables ? '✓' : '+'}</span>
                    {t('request_combine_tables', 'Request to combine these tables')}
                  </button>
                </div>
              )}

              {/* Customer Details */}
              <div className={styles.formSection}>
                <label className={styles.label}>{t('your_details', 'Your Details')}</label>

                <input
                  type="text"
                  placeholder={t('your_name', 'Your Name')}
                  value={customerName}
                  onChange={(e) => setCustomerName(e.target.value)}
                  className={styles.input}
                  required
                />

                <input
                  type="email"
                  placeholder={t('your_email', 'Your Email')}
                  value={customerEmail}
                  onChange={(e) => setCustomerEmail(e.target.value)}
                  className={styles.input}
                  required
                />

                <input
                  type="tel"
                  placeholder={t('your_phone_optional', 'Your Phone (Optional)')}
                  value={customerPhone}
                  onChange={(e) => setCustomerPhone(e.target.value)}
                  className={styles.input}
                />

                <textarea
                  placeholder={t('special_requests_placeholder', 'Allergies, dietary requirements, special occasions, etc.')}
                  value={specialRequests}
                  onChange={(e) => setSpecialRequests(e.target.value)}
                  className={styles.textarea}
                  rows={3}
                />
              </div>

              {/* Submit Button */}
              <button
                type="submit"
                className={styles.bookButton}
                disabled={!canSubmit || submitting}
              >
                {submitting ? t('booking', 'Booking...') : t('book_now', 'Book Now')}
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
