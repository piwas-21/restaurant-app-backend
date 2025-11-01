"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { reservationService } from '@/services/reservationService';
import { ReservationDto, ReservationStatus, ReservationStatusLabel } from '@/types/reservation';
import styles from './MyReservations.module.css';
import statusStyles from '../../styles/orderStatus.module.css';
import { Calendar, Clock, Users, MapPin, ChevronDown, ChevronUp } from 'lucide-react';

export default function MyReservations() {
  const { t } = useTranslation();
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedReservation, setExpandedReservation] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState<string | null>(null);

  useEffect(() => {
    loadReservations();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const loadReservations = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await reservationService.getReservations();
      setReservations(result.items);
    } catch (err: any) {
      setError(err.message || t('my_reservations_error', 'Failed to load reservations'));
      setReservations([]);
    } finally {
      setLoading(false);
    }
  };

  const handleCancelReservation = async (id: string) => {
    if (!confirm(t('my_reservations_cancel_confirm', 'Are you sure you want to cancel this reservation?'))) {
      return;
    }

    setCancelling(id);
    try {
      await reservationService.cancelReservation(id);
      alert(t('my_reservations_cancelled_success', 'Reservation cancelled successfully!'));
      loadReservations();
    } catch (err: any) {
      alert(err.message || t('my_reservations_cancel_error', 'Failed to cancel reservation'));
    } finally {
      setCancelling(null);
    }
  };

  const toggleExpanded = (id: string) => {
    setExpandedReservation(expandedReservation === id ? null : id);
  };

  const getStatusClass = (status: typeof ReservationStatusLabel[keyof typeof ReservationStatusLabel]): string => {
    switch (status) {
      case ReservationStatusLabel[ReservationStatus.Confirmed]:
        return statusStyles.statusConfirmed;
      case ReservationStatusLabel[ReservationStatus.Pending]:
        return statusStyles.statusPending;
      case ReservationStatusLabel[ReservationStatus.Cancelled]:
        return statusStyles.statusCancelled;
      case ReservationStatusLabel[ReservationStatus.Completed]:
        return statusStyles.statusCompleted;
      case ReservationStatusLabel[ReservationStatus.NoShow]:
        return statusStyles.statusNoShow;
      default:
        return '';
    }
  };

  const getStatusText = (status: ReservationStatus): string => {
    return t(`my_reservations_status_${ReservationStatus[status]}`, ReservationStatus[status]);
  };

  const formatDate = (dateString: string): string => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      weekday: 'short',
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  };

  const formatTime = (timeString: string): string => {
    const parts = timeString.split(':');
    return `${parts[0]}:${parts[1]}`;
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <section className={styles.section}>
          <h2 className={styles.sectionTitle}>{t('reservations_title', 'My Reservations')}</h2>
          <p className={styles.loadingText}>{t('my_reservations_loading', 'Loading...')}</p>
        </section>
      </div>
    );
  }

  if (error) {
    return (
      <div className={styles.container}>
        <section className={styles.section}>
          <h2 className={styles.sectionTitle}>{t('reservations_title', 'My Reservations')}</h2>
          <p className={styles.error}>{error}</p>
        </section>
      </div>
    );
  }

  if (reservations.length === 0) {
    return (
      <div className={styles.container}>
        <section className={styles.section}>
          <h2 className={styles.sectionTitle}>{t('reservations_title', 'My Reservations')}</h2>
          <p className={styles.emptyMessage}>
            {t('no_reservations_message', 'You have no reservations yet.')}
          </p>
        </section>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>{t('reservations_title', 'My Reservations')}</h2>

        <div className={styles.reservationsList}>
          {reservations.map((reservation: any) => (
            <div key={reservation.id} className={styles.reservationCard}>
              <div className={styles.reservationHeader}>
                <div className={styles.reservationMainInfo}>
                  <div className={styles.dateTimeInfo}>
                    <div className={styles.infoItem}>
                      <Calendar size={16} className={styles.icon} />
                      <span className={styles.infoText}>{formatDate(reservation.reservationDate)}</span>
                    </div>
                    <div className={styles.infoItem}>
                      <Clock size={16} className={styles.icon} />
                      <span className={styles.infoText}>{formatTime(reservation.startTime)} - {formatTime(reservation.endTime)}</span>
                    </div>
                    <div className={styles.infoItem}>
                      <Users size={16} className={styles.icon} />
                      <span className={styles.infoText}>
                        {reservation.numberOfGuests} {t('guests', 'guests')}
                      </span>
                    </div>
                  </div>

                  <span className={`${statusStyles.statusBadge} ${getStatusClass(reservation.status)}`}>
                    {getStatusText(reservation.status)}
                  </span>
                </div>

                <button
                  className={styles.expandButton}
                  onClick={() => toggleExpanded(reservation.id)}
                  aria-label={t('toggle_details', 'Toggle details')}
                >
                  {expandedReservation === reservation.id ? (
                    <ChevronUp size={20} />
                  ) : (
                    <ChevronDown size={20} />
                  )}
                </button>
              </div>

              {expandedReservation === reservation.id && (
                <div className={styles.reservationDetails}>
                  <div className={styles.detailItem}>
                    <MapPin size={16} className={styles.icon} />
                    <span className={styles.detailLabel}>{t('restaurant', 'Restaurant')}:</span>
                    <span className={styles.detailValue}>Rumi Restaurant</span>
                  </div>

                  {reservation.tableNumber && (
                    <div className={styles.detailItem}>
                      <span className={styles.detailLabel}>{t('table_number', 'Table')}:</span>
                      <span className={styles.detailValue}>{reservation.tableNumber}</span>
                    </div>
                  )}

                  {reservation.specialRequests && (
                    <div className={styles.detailItem}>
                      <span className={styles.detailLabel}>{t('special_requests', 'Special Requests')}:</span>
                      <span className={styles.detailValue}>{reservation.specialRequests}</span>
                    </div>
                  )}

                  {reservation.notes && (
                    <div className={styles.detailItem}>
                      <span className={styles.detailLabel}>{t('my_reservations_admin_notes', 'Admin Notes')}:</span>
                      <span className={styles.detailValue}>{reservation.notes}</span>
                    </div>
                  )}

                  {(reservation.status === ReservationStatus.Pending ||
                    reservation.status === ReservationStatus.Confirmed) && (
                    <div className={styles.actions}>
                      <button
                        className={styles.cancelButton}
                        onClick={() => handleCancelReservation(reservation.id)}
                        disabled={cancelling === reservation.id}
                      >
                        {cancelling === reservation.id
                          ? t('my_reservations_cancelling', 'Cancelling...')
                          : t('cancel_reservation', 'Cancel Reservation')}
                      </button>
                      <button className={styles.modifyButton}>
                        {t('modify_reservation', 'Modify')}
                      </button>
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
