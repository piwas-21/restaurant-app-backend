'use client';

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
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
  const { user } = useAuth();

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

  // Load all tables on mount
  useEffect(() => {
    loadAllTables();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Prefill customer details from logged-in user
  useEffect(() => {
    if (user) {
      setCustomerName(`${user.firstName} ${user.lastName}`.trim());
      setCustomerEmail(user.email || '');
      // Phone not available in User interface
    }
  }, [user]);

  // Check availability when date or guests change
  useEffect(() => {
    if (selectedDate) {
      fetchTimeSlots();
    } else {
      setAvailableTimeSlots([]);
      setBookedTableIds([]);
      setCapacityWarning('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedDate, numberOfGuests]);

  // Update booked tables when time changes or slots update
  useEffect(() => {
    if (selectedDate && availableTimeSlots.length > 0) {
      if (selectedTime) {
        // Check if selected time is still valid
        const slotExists = availableTimeSlots.some((s: any) => s.startTime.startsWith(selectedTime));
        if (!slotExists) {
          setSelectedTime('');
          setBookedTableIds([]);
          return;
        }
        updateTableAvailability();
      }
    } else {
      setBookedTableIds([]);
      setCapacityWarning('');
    }
  }, [selectedTime, availableTimeSlots]);

  const loadAllTables = async () => {
    try {
      const tables = await reservationService.getTables(true); // Only active tables
      setAllTables(tables);
    } catch {
      enqueueSnackbar(t('failed_to_load_tables', 'Failed to load tables'), { variant: 'error' });
    }
  };

  const fetchTimeSlots = async () => {
    if (!selectedDate) return;

    setLoading(true);
    setCapacityWarning(''); // Clear previous warnings

    try {
      const result = await reservationService.getAvailableTimeSlots(selectedDate, numberOfGuests);

      // Handle API errors
      if (result.error || !result.data) {
        setAvailableTimeSlots([]);
        return;
      }

      // Store all time slots for showing available times
      const timeSlots = result.data.timeSlots || [];
      setAvailableTimeSlots(timeSlots);

      // Check if restaurant is closed on this day (no time slots)
      if (timeSlots.length === 0) {
        const dateObj = new Date(selectedDate);
        const dayName = dateObj.toLocaleDateString('en-US', { weekday: 'long' });
        enqueueSnackbar(
          t('restaurant_closed_on_date', 'Restaurant is closed on {{day}}, {{date}}. Please select another date.', {
            day: dayName,
            date: dateObj.toLocaleDateString()
          }),
          { variant: 'warning', autoHideDuration: 5000 }
        );
        // Clear the selected date to prompt user to choose another
        setSelectedDate('');
      }
    } catch {
      setAvailableTimeSlots([]);
    } finally {
      setLoading(false);
    }
  };

  const updateTableAvailability = () => {
    // Find the time slot that matches the selected time
    // API returns times like "12:00:00", we match start
    const slot = availableTimeSlots.find(s => s.startTime.startsWith(selectedTime));

    if (slot) {
      // We found the selected time slot - check availability
      const availableIds = new Set(slot.availableTables.map((t: any) => t.id));
      const booked = allTables.filter(t => !availableIds.has(t.id)).map(t => t.id);
      setBookedTableIds(booked);

      // Check capacity: are there any tables that can accommodate the party size?
      const tablesWithCapacity = slot.availableTables.filter((t: any) => t.maxGuests >= numberOfGuests);

      if (tablesWithCapacity.length === 0 && slot.availableTables.length > 0) {
        // Tables are available but none have sufficient capacity
        setCapacityWarning(
          t('capacity_warning_message',
            'We don\'t have a single table that can accommodate all {{guests}} guests. However, you can select multiple tables and request to combine them, or proceed with your selection and our staff will review your request to find the best arrangement.',
            { guests: numberOfGuests }
          )
        );
      }
    } else {
      // Selected time slot not available in the list
      // This implies the time is fully booked or restaurant closed at this time
      // The user shouldn't be able to select this time ideally, but if they did (via old state):
      setBookedTableIds(allTables.map(t => t.id));
    }

    // Check if guest size exceeds ALL tables in restaurant (not just available ones)
    if (allTables.length > 0) {
      const maxRestaurantCapacity = Math.max(...allTables.map(t => t.maxGuests));
      if (numberOfGuests > maxRestaurantCapacity) {
        setCapacityWarning(
          t('capacity_warning_message',
            'We don\'t have a single table that can accommodate all {{guests}} guests. However, you can select multiple tables and request to combine them, or proceed with your selection and our staff will review your request to find the best arrangement.',
            { guests: numberOfGuests }
          )
        );
      }
    }
  };

  const handleTableSelect = (table: TableDto) => {
    const isBooked = bookedTableIds.includes(table.id);
    const isSelected = selectedTableIds.includes(table.id);

    // If table is booked and not already selected, prevent selection and show info
    if (isBooked && !isSelected) {
      if (selectedDate && availableTimeSlots.length > 0) {
        const availableTimes = availableTimeSlots
          .filter(slot => slot.availableTables.some((t: TableDto) => t.id === table.id))
          .map(slot => {
            const start = slot.startTime.substring(0, 5); // HH:mm
            return start;
          });

        if (availableTimes.length > 0) {
          enqueueSnackbar(
            t('table_booked_available_at', 'Table {{tableNumber}} is booked at this time. Available at: {{times}}', {
              tableNumber: table.tableNumber,
              times: availableTimes.join(', ')
            }),
            { variant: 'info', autoHideDuration: 5000 }
          );
        } else {
          enqueueSnackbar(
            t('table_not_available_today', 'Table {{tableNumber}} is not available today for {{guests}} guests', {
              tableNumber: table.tableNumber,
              guests: numberOfGuests
            }),
            { variant: 'warning' }
          );
        }
      } else {
        enqueueSnackbar(
          t('table_booked', 'Table {{tableNumber}} is currently booked', {
            tableNumber: table.tableNumber
          }),
          { variant: 'warning' }
        );
      }
      // Do NOT allow selection of booked tables
      return;
    }

    // Allow selection/deselection for available tables only
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

    // Re-validate table availability before submission
    const unavailableTables = selectedTableIds.filter(id => bookedTableIds.includes(id));
    if (unavailableTables.length > 0) {
      const tableNumbers = unavailableTables
        .map(id => allTables.find(t => t.id === id)?.tableNumber)
        .filter(Boolean)
        .join(', ');
      enqueueSnackbar(
        t('selected_tables_not_available', 'Selected table(s) {{tables}} are no longer available for this time slot. Please select different tables or time.', { tables: tableNumbers }),
        { variant: 'error' }
      );
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
      // Extract detailed error message from API response
      let errorMessage = t('reservation_failed', 'Failed to create reservation');

      // Try to get the specific error message from the API
      if (err?.response?.data?.errors && Array.isArray(err.response.data.errors) && err.response.data.errors.length > 0) {
        // Show the first specific error from the API errors array
        errorMessage = err.response.data.errors[0];
      } else if (err?.response?.data?.message && err.response.data.message !== 'Operation failed') {
        // Use the message field if it's not the generic "Operation failed"
        errorMessage = err.response.data.message;
      } else if (err?.message && err.message !== 'Request failed with status code 400') {
        // Use error message if it's not generic
        errorMessage = err.message;
      }

      console.error('Reservation error:', err?.response?.data || err); // Log for debugging

      enqueueSnackbar(
        errorMessage,
        { variant: 'error', autoHideDuration: 6000 }
      );
    } finally {
      setSubmitting(false);
    }
  };

  const selectedTables = allTables.filter(t => selectedTableIds.includes(t.id));

  // Filter time slots based on selected tables
  // If tables are selected, only show times when ALL selected tables are available
  const filteredTimeSlots = selectedTableIds.length > 0
    ? availableTimeSlots
        .filter(slot => {
          // Check if all selected tables are available in this time slot
          const slotTableIds = slot.availableTables.map((t: any) => t.id);
          return selectedTableIds.every(selectedId => slotTableIds.includes(selectedId));
        })
        .map((s: any) => s.startTime.substring(0, 5)) // HH:mm
    : availableTimeSlots.map((s: any) => s.startTime.substring(0, 5)); // Show all slots if no table selected

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
                  ({t('tables_count_selected', '{{count}} selected', { count: selectedTableIds.length })})
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
                availableTimeSlots={filteredTimeSlots}
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
        customerEmail={customerEmail}
        numberOfTables={selectedTableIds.length}
      />
    </div>
  );
}
