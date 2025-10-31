'use client';

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { reservationService } from '@/services/reservationService';
import {
  ReservationFormData,
  TimeSlotDto,
  TableDto,
  AvailableTimeSlotsDto
} from '@/types/reservation';
import styles from './ReservationWizard.module.css';

type WizardStep = 'guests' | 'date' | 'time' | 'table' | 'details' | 'summary';

export default function ReservationWizard() {
  const { t } = useTranslation();
  const [currentStep, setCurrentStep] = useState<WizardStep>('guests');
  const [formData, setFormData] = useState<ReservationFormData>({
    customerName: '',
    customerEmail: '',
    customerPhone: '',
    numberOfGuests: 2,
    reservationDate: new Date(),
    specialRequests: ''
  });
  const [availableSlots, setAvailableSlots] = useState<AvailableTimeSlotsDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const steps: WizardStep[] = ['guests', 'date', 'time', 'table', 'details', 'summary'];
  const currentStepIndex = steps.indexOf(currentStep);

  const handleNext = () => {
    if (currentStepIndex < steps.length - 1) {
      setCurrentStep(steps[currentStepIndex + 1]);
    }
  };

  const handleBack = () => {
    if (currentStepIndex > 0) {
      setCurrentStep(steps[currentStepIndex - 1]);
    }
  };

  const loadAvailableSlots = async (date: Date, guests: number) => {
    setLoading(true);
    setError(null);
    try {
      const dateStr = date.toISOString().split('T')[0];
      const result = await reservationService.getAvailableTimeSlots(dateStr, guests);
      if (result.error) {
        setError(result.error);
        setAvailableSlots(null);
      } else {
        setAvailableSlots(result.data);
      }
    } catch (err) {
      setError('Failed to load available time slots');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleDateChange = async (date: Date) => {
    setFormData({ ...formData, reservationDate: date, selectedTimeSlot: undefined, selectedTable: undefined });
    await loadAvailableSlots(date, formData.numberOfGuests);
  };

  const handleGuestsChange = async (guests: number) => {
    setFormData({ ...formData, numberOfGuests: guests, selectedTimeSlot: undefined, selectedTable: undefined });
    if (currentStep !== 'guests') {
      await loadAvailableSlots(formData.reservationDate, guests);
    }
  };

  const handleSubmit = async () => {
    if (!formData.selectedTimeSlot || !formData.selectedTable) {
      setError('Please complete all required fields');
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const reservationData = {
        customerName: formData.customerName,
        customerEmail: formData.customerEmail,
        customerPhone: formData.customerPhone,
        tableId: formData.selectedTable.id,
        reservationDate: formData.reservationDate.toISOString(),
        startTime: formData.selectedTimeSlot.startTime,
        endTime: formData.selectedTimeSlot.endTime,
        numberOfGuests: formData.numberOfGuests,
        specialRequests: formData.specialRequests || undefined
      };

      await reservationService.createReservation(reservationData);

      // Show success message and reset
      alert(t('reservation_success_message'));
      window.location.href = '/';
    } catch (err: any) {
      setError(err.message || t('reservation_error'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.wizard}>
      <div className={styles.header}>
        <h1>{t('reservation_title')}</h1>
        <p>{t('reservation_subtitle')}</p>
      </div>

      <div className={styles.progressBar}>
        {steps.map((step, index) => (
          <div
            key={step}
            className={`${styles.progressStep} ${
              index <= currentStepIndex ? styles.active : ''
            }`}
          >
            <div className={styles.progressCircle}>{index + 1}</div>
            <span className={styles.progressLabel}>{t(`reservation_step_${step}`)}</span>
          </div>
        ))}
      </div>

      <div className={styles.content}>
        {error && <div className={styles.error}>{error}</div>}

        {/* Step 1: Guests */}
        {currentStep === 'guests' && (
          <div className={styles.step}>
            <h2>{t('reservation_how_many_guests')}</h2>
            <div className={styles.guestSelector}>
              {[1, 2, 3, 4, 5, 6].map((count) => (
                <button
                  key={count}
                  className={`${styles.guestButton} ${
                    formData.numberOfGuests === count ? styles.selected : ''
                  }`}
                  onClick={() => handleGuestsChange(count)}
                >
                  {count} {count === 1 ? 'Guest' : 'Guests'}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Step 2: Date */}
        {currentStep === 'date' && (
          <div className={styles.step}>
            <h2>{t('reservation_select_date')}</h2>
            <input
              type="date"
              className={styles.dateInput}
              value={formData.reservationDate.toISOString().split('T')[0]}
              min={new Date().toISOString().split('T')[0]}
              onChange={(e) => handleDateChange(new Date(e.target.value))}
            />
          </div>
        )}

        {/* Step 3: Time Slot */}
        {currentStep === 'time' && (
          <div className={styles.step}>
            <h2>{t('reservation_select_time_slot')}</h2>
            {loading ? (
              <div>Loading...</div>
            ) : availableSlots && availableSlots.timeSlots.length > 0 ? (
              <div className={styles.timeSlots}>
                {availableSlots.timeSlots.map((slot, index) => (
                  <button
                    key={index}
                    className={`${styles.timeSlotButton} ${
                      formData.selectedTimeSlot === slot ? styles.selected : ''
                    }`}
                    onClick={() => {
                      setFormData({ ...formData, selectedTimeSlot: slot, selectedTable: undefined });
                      handleNext();
                    }}
                  >
                    {reservationService.formatTimeSlot(slot.startTime, slot.endTime)}
                    <span className={styles.availableCount}>
                      {slot.availableTables.length} tables available
                    </span>
                  </button>
                ))}
              </div>
            ) : (
              <p>{t('reservation_no_slots_available')}</p>
            )}
          </div>
        )}

        {/* Step 4: Table Selection */}
        {currentStep === 'table' && formData.selectedTimeSlot && (
          <div className={styles.step}>
            <h2>{t('reservation_select_table')}</h2>
            <div className={styles.tables}>
              {formData.selectedTimeSlot.availableTables.map((table) => (
                <button
                  key={table.id}
                  className={`${styles.tableCard} ${
                    formData.selectedTable?.id === table.id ? styles.selected : ''
                  }`}
                  onClick={() => setFormData({ ...formData, selectedTable: table })}
                >
                  <div className={styles.tableNumber}>
                    {t('reservation_table_number', { number: table.tableNumber })}
                  </div>
                  <div className={styles.tableDetails}>
                    <span>{t('reservation_table_capacity', { maxGuests: table.maxGuests })}</span>
                    <span className={styles.tableLocation}>
                      {table.isOutdoor ? t('reservation_table_outdoor') : t('reservation_table_indoor')}
                    </span>
                  </div>
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Step 5: Customer Details */}
        {currentStep === 'details' && (
          <div className={styles.step}>
            <h2>{t('reservation_customer_details')}</h2>
            <div className={styles.form}>
              <div className={styles.formGroup}>
                <label>{t('reservation_name_label')}</label>
                <input
                  type="text"
                  value={formData.customerName}
                  onChange={(e) => setFormData({ ...formData, customerName: e.target.value })}
                  placeholder={t('reservation_name_placeholder')}
                  required
                />
              </div>
              <div className={styles.formGroup}>
                <label>{t('reservation_email_label')}</label>
                <input
                  type="email"
                  value={formData.customerEmail}
                  onChange={(e) => setFormData({ ...formData, customerEmail: e.target.value })}
                  placeholder={t('reservation_email_placeholder')}
                  required
                />
              </div>
              <div className={styles.formGroup}>
                <label>{t('reservation_phone_label')}</label>
                <input
                  type="tel"
                  value={formData.customerPhone}
                  onChange={(e) => setFormData({ ...formData, customerPhone: e.target.value })}
                  placeholder={t('reservation_phone_placeholder')}
                  required
                />
              </div>
              <div className={styles.formGroup}>
                <label>{t('reservation_special_requests')}</label>
                <textarea
                  value={formData.specialRequests}
                  onChange={(e) => setFormData({ ...formData, specialRequests: e.target.value })}
                  placeholder={t('reservation_special_requests_placeholder')}
                  rows={4}
                />
              </div>
            </div>
          </div>
        )}

        {/* Step 6: Summary */}
        {currentStep === 'summary' && (
          <div className={styles.step}>
            <h2>{t('reservation_summary')}</h2>
            <div className={styles.summary}>
              <div className={styles.summaryItem}>
                <strong>{t('reservation_step_guests')}:</strong>
                <span>{t('reservation_summary_guests', { count: formData.numberOfGuests })}</span>
              </div>
              <div className={styles.summaryItem}>
                <strong>{t('reservation_summary_date')}:</strong>
                <span>{formData.reservationDate.toLocaleDateString()}</span>
              </div>
              {formData.selectedTimeSlot && (
                <div className={styles.summaryItem}>
                  <strong>{t('reservation_summary_time')}:</strong>
                  <span>
                    {reservationService.formatTimeSlot(
                      formData.selectedTimeSlot.startTime,
                      formData.selectedTimeSlot.endTime
                    )}
                  </span>
                </div>
              )}
              {formData.selectedTable && (
                <div className={styles.summaryItem}>
                  <strong>{t('reservation_summary_table')}:</strong>
                  <span>{formData.selectedTable.tableNumber}</span>
                </div>
              )}
              <div className={styles.summaryItem}>
                <strong>{t('reservation_name_label')}:</strong>
                <span>{formData.customerName}</span>
              </div>
              <div className={styles.summaryItem}>
                <strong>{t('reservation_email_label')}:</strong>
                <span>{formData.customerEmail}</span>
              </div>
              <div className={styles.summaryItem}>
                <strong>{t('reservation_phone_label')}:</strong>
                <span>{formData.customerPhone}</span>
              </div>
              {formData.specialRequests && (
                <div className={styles.summaryItem}>
                  <strong>{t('reservation_special_requests')}:</strong>
                  <span>{formData.specialRequests}</span>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      <div className={styles.actions}>
        {currentStepIndex > 0 && (
          <button className={styles.backButton} onClick={handleBack} disabled={submitting}>
            {t('reservation_back_button')}
          </button>
        )}
        {currentStep !== 'summary' && currentStep !== 'time' && (
          <button
            className={styles.nextButton}
            onClick={() => {
              if (currentStep === 'date') {
                loadAvailableSlots(formData.reservationDate, formData.numberOfGuests);
              }
              handleNext();
            }}
            disabled={loading}
          >
            {t('reservation_next_button')}
          </button>
        )}
        {currentStep === 'summary' && (
          <button
            className={styles.confirmButton}
            onClick={handleSubmit}
            disabled={submitting}
          >
            {submitting ? t('reservation_creating') : t('reservation_confirm_button')}
          </button>
        )}
      </div>
    </div>
  );
}
