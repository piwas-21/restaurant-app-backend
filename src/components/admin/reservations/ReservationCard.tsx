import React from 'react';
import { Calendar, Clock, MapPin, Users, Check, X, Trash2 } from 'lucide-react';
import { ReservationDto, ReservationStatus } from '@/types/reservation';
import { reservationService } from '@/services/reservationService';
import styles from './ReservationCard.module.css';

interface ReservationCardProps {
  reservation: ReservationDto;
  isSelected: boolean;
  onToggleSelection: (id: string) => void;
  onConfirm: (id: string) => void;
  onCancel: (id: string) => void;
  onDelete: (id: string) => void;
  tableLabel: string;
  guestsLabel: string;
  specialRequestsLabel: string;
  confirmLabel: string;
  cancelLabel: string;
  deleteLabel: string;
}

export const ReservationCard: React.FC<ReservationCardProps> = ({
  reservation,
  isSelected,
  onToggleSelection,
  onConfirm,
  onCancel,
  onDelete,
  tableLabel,
  guestsLabel,
  specialRequestsLabel,
  confirmLabel,
  cancelLabel,
  deleteLabel,
}) => {
  const statusLabel = reservationService.getStatusLabel(reservation.status) || 'pending';
  const dataStatus = statusLabel.toLowerCase().replace(/\s+/g, '');

  const getStatusBadgeClass = (status: ReservationStatus) => {
    switch (status) {
      case ReservationStatus.Confirmed:
        return styles.statusConfirmed;
      case ReservationStatus.Pending:
        return styles.statusPending;
      case ReservationStatus.Cancelled:
        return styles.statusCancelled;
      case ReservationStatus.Completed:
        return styles.statusCompleted;
      case ReservationStatus.NoShow:
        return styles.statusNoShow;
      default:
        return '';
    }
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  };

  const formatTime = (timeString: string) => {
    const parts = timeString.split(':');
    return `${parts[0]}:${parts[1]}`;
  };

  return (
    <div
      className={`${styles.reservationCard} ${isSelected ? styles.selected : ''}`}
      data-status={dataStatus}
      onClick={(e) => {
        // Don't toggle if clicking on action buttons
        if ((e.target as HTMLElement).closest('button')) {
          return;
        }
        onToggleSelection(reservation.id);
      }}
    >
      <div className={styles.cardHeader}>
        <div className={styles.customerInfo}>
          <h3>{reservation.customerName}</h3>
          <div className={styles.contactInfo}>
            <span>{reservation.customerEmail}</span>
            {reservation.customerPhone && <span>{reservation.customerPhone}</span>}
          </div>
        </div>
        <span className={`${styles.statusBadge} ${getStatusBadgeClass(reservation.status)}`}>
          {statusLabel}
        </span>
      </div>

      <div className={styles.cardBody}>
        <div className={styles.infoRow}>
          <div className={styles.infoItem}>
            <Calendar size={16} />
            <span>{formatDate(reservation.reservationDate)}</span>
          </div>
          <div className={styles.infoItem}>
            <Clock size={16} />
            <span>{formatTime(reservation.startTime)} - {formatTime(reservation.endTime)}</span>
          </div>
          <div className={styles.infoItem}>
            <MapPin size={16} />
            <span>{tableLabel} {reservation.tableNumber}</span>
          </div>
          <div className={styles.infoItem}>
            <Users size={16} />
            <span>{reservation.numberOfGuests} {guestsLabel}</span>
          </div>
        </div>

        {reservation.specialRequests && (
          <div className={styles.specialRequests}>
            <strong>{specialRequestsLabel}:</strong>
            <p>{reservation.specialRequests}</p>
          </div>
        )}
      </div>

      <div className={styles.cardActions}>
        {reservation.status === ReservationStatus.Pending && (
          <>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onConfirm(reservation.id);
              }}
              className={styles.confirmButton}
            >
              <Check size={16} />
              {confirmLabel}
            </button>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onCancel(reservation.id);
              }}
              className={styles.cancelButton}
            >
              <X size={16} />
              {cancelLabel}
            </button>
          </>
        )}
        {reservation.status === ReservationStatus.Confirmed && (
          <button
            onClick={(e) => {
              e.stopPropagation();
              onCancel(reservation.id);
            }}
            className={styles.cancelButton}
          >
            <X size={16} />
            {cancelLabel}
          </button>
        )}
        <button
          onClick={(e) => {
            e.stopPropagation();
            onDelete(reservation.id);
          }}
          className={styles.deleteButton}
        >
          <Trash2 size={16} />
          {deleteLabel}
        </button>
      </div>
    </div>
  );
};
