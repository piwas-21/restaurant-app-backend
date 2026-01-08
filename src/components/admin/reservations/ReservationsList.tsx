import React from 'react';
import { ReservationDto } from '@/types/reservation';
import { ReservationCard } from './ReservationCard';
import styles from './ReservationsList.module.css';

interface ReservationsListProps {
  reservations: ReservationDto[];
  selectedReservationIds: Set<string>;
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

export const ReservationsList: React.FC<ReservationsListProps> = ({
  reservations,
  selectedReservationIds,
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
  return (
    <div className={styles.reservationsList}>
      {reservations.map(reservation => (
        <ReservationCard
          key={reservation.id}
          reservation={reservation}
          isSelected={selectedReservationIds.has(reservation.id)}
          onToggleSelection={onToggleSelection}
          onConfirm={onConfirm}
          onCancel={onCancel}
          onDelete={onDelete}
          tableLabel={tableLabel}
          guestsLabel={guestsLabel}
          specialRequestsLabel={specialRequestsLabel}
          confirmLabel={confirmLabel}
          cancelLabel={cancelLabel}
          deleteLabel={deleteLabel}
        />
      ))}
    </div>
  );
};
