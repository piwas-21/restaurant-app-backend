'use client';

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { reservationService } from '@/services/reservationService';
import { TableDto } from '@/types/reservation';
import VisualTableLayout from '@/components/reservation/VisualTableLayout';
import GuestSelector from '@/components/reservation/GuestSelector';
import DateTimeSelector from '@/components/reservation/DateTimeSelector';
import CustomerDetailsForm from '@/components/reservation/CustomerDetailsForm';
import CapacityWarning from '@/components/reservation/CapacityWarning';
import SelectedTableInfo from '@/components/reservation/SelectedTableInfo';
import ReservationSuccessModal from '@/components/reservation/ReservationSuccessModal';
import styles from './ReservationsPage.module.css';
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
  const [bookedTableIds, setBookedTableIds] = useState<string[]>([]);
  const [availableTimeSlots, setAvailableTimeSlots] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [capacityWarning, setCapacityWarning] = useState<string>('');

  // Success modal state
  const [showSuccessModal, setShowSuccessModal] = useState(false);
  const [isLoggedIn, setIsLoggedIn] = useState(false);

  // Load all tables on mount
  useEffect(() => {
    loadAllTables();

    // Check if user is logged in
    const token = localStorage.getItem('token');
    setIsLoggedIn(!!token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Check availability when date/time/guests change
  useEffect(() => {
    if (selectedDate && selectedTime) {
      checkAvailability();
    } else {
      // If no date/time selected, reset booked tables (all tables available)
      setBookedTableIds([]);
      setCapacityWarning('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedDate, selectedTime, numberOfGuests]);

  const loadAllTables = async () => {
    try {
      const tables = await reservationService.getTables(true); // Only active tables
      setAllTables(tables);
    } catch {
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
        // Show all tables as available with a warning
        setCapacityWarning(result.error);
        setBookedTableIds([]);
      } else if (result.error) {
        // Other API errors - mark all tables as booked
        setBookedTableIds(allTables.map(t => t.id));
      } else if (result.data) {
        // Store all time slots for showing available times
        setAvailableTimeSlots(result.data.timeSlots || []);

        // Find the time slot that matches the selected time
        const slot = result.data.timeSlots.find(s => s.startTime.startsWith(selectedTime));

        if (slot) {
          // Calculate booked tables for the selected time slot
          const availableIds = new Set(slot.availableTables.map(t => t.id));
          const booked = allTables.filter(t => !availableIds.has(t.id)).map(t => t.id);
          setBookedTableIds(booked);

          // Check if any available tables meet the capacity requirement
          const suitableTables = slot.availableTables.filter(t => t.maxGuests >= numberOfGuests);
          if (suitableTables.length === 0 && slot.availableTables.length > 0) {
            // Tables are available but none meet capacity - show warning
            setCapacityWarning(`Available tables may not accommodate ${numberOfGuests} guests individually. Consider selecting multiple tables.`);
          }
        } else {
          // Selected time slot not found - check if tables exist at other times
          // This prevents falsely showing all tables as booked when they're just at different times
          const allAvailableTableIds = new Set<string>();
          result.data.timeSlots.forEach(timeSlot => {
            timeSlot.availableTables.forEach(t => allAvailableTableIds.add(t.id));
          });

          if (allAvailableTableIds.size > 0) {
            // Some tables are available at other times - only mark truly booked ones
            const booked = allTables.filter(t => !allAvailableTableIds.has(t.id)).map(t => t.id);
            setBookedTableIds(booked);
            setCapacityWarning('Some tables may be booked at this time. Check availability at other times or consider combining tables.');
          } else {
            // No tables available at any time
            setBookedTableIds(allTables.map(t => t.id));
          }
        }
      }
    } catch {
      // Unexpected network errors
      setBookedTableIds(allTables.map(t => t.id));
    } finally {
      setLoading(false);
    }
  };  const handleTableSelect = (table: TableDto) => {
    const isBooked = bookedTableIds.includes(table.id);
    const isSelected = selectedTableIds.includes(table.id);

    // If table is booked and not selected, show available times (but still allow selection)
    if (isBooked && !isSelected && selectedDate && availableTimeSlots.length > 0) {
      const availableTimes = availableTimeSlots
        .filter(slot => slot.availableTables.some((t: TableDto) => t.id === table.id))
        .map(slot => {
          const start = slot.startTime.substring(0, 5); // HH:mm
          return start;
        });

      if (availableTimes.length > 0) {
        enqueueSnackbar(
          `Table ${table.tableNumber} is booked at this time. Available at: ${availableTimes.join(', ')}`,
          { variant: 'info', autoHideDuration: 5000 }
        );
      } else {
        enqueueSnackbar(
          `Table ${table.tableNumber} is not available today for ${numberOfGuests} guests`,
          { variant: 'warning' }
        );
      }
      // Still allow selection even for booked tables
    }

    // Allow selection/deselection for ALL tables
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

  const handleCloseSuccessModal = () => {
    setShowSuccessModal(false);
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
          customerPhone: customerPhone.trim() || "", // Send empty string if empty
          tableId,
          reservationDate: new Date(selectedDate).toISOString(),
          startTime: `${selectedTime}:00`,
          endTime: `${parseInt(selectedTime.split(':')[0]) + 2}:00:00`, // 2-hour reservation
          numberOfGuests,
          specialRequests: finalSpecialRequests || null
        };
        return reservationService.createReservation(reservationData);
      });

      await Promise.all(reservationPromises);

      // Show success modal instead of redirecting
      setShowSuccessModal(true);

      // Reset form except email and name (user might want to make another reservation)
      setSelectedTableIds([]);
      setRequestCombineTables(false);
      setSpecialRequests('');
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
                <span className={styles.selectionCount}>
                  ({selectedTableIds.length} {selectedTableIds.length === 1 ? t('table_selected', 'table') : t('tables_selected', 'tables')} selected)
                </span>
              )}
            </h2>

            {/* Capacity Warning */}
            {capacityWarning && <CapacityWarning numberOfGuests={numberOfGuests} />}

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
              <GuestSelector
                numberOfGuests={numberOfGuests}
                onGuestsChange={setNumberOfGuests}
              />

              <DateTimeSelector
                selectedDate={selectedDate}
                selectedTime={selectedTime}
                onDateChange={setSelectedDate}
                onTimeChange={setSelectedTime}
                loading={loading}
              />

              <SelectedTableInfo
                selectedTables={selectedTables}
                requestCombineTables={requestCombineTables}
                onToggleCombine={() => setRequestCombineTables(!requestCombineTables)}
              />

              <CustomerDetailsForm
                customerName={customerName}
                customerEmail={customerEmail}
                customerPhone={customerPhone}
                specialRequests={specialRequests}
                onNameChange={setCustomerName}
                onEmailChange={setCustomerEmail}
                onPhoneChange={setCustomerPhone}
                onSpecialRequestsChange={setSpecialRequests}
              />

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

      {/* Success Modal */}
      <ReservationSuccessModal
        isOpen={showSuccessModal}
        onClose={handleCloseSuccessModal}
        isLoggedIn={isLoggedIn}
        customerEmail={customerEmail}
        numberOfTables={selectedTableIds.length}
      />
    </div>
  );
}
