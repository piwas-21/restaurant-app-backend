// src/app/reservations/page.tsx
"use client";

import React, { useState } from "react";
import styles from "../styles/Reservations.module.css"; // Use desktop styles
import { useTranslation } from "react-i18next";

export default function ReservationsPage() { // Changed component name
  const { t } = useTranslation();
  const [date, setDate] = useState("");
  const [time, setTime] = useState("");
  const [guests, setGuests] = useState("2");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [notes, setNotes] = useState("");

  const handleSubmitReservation = (e: React.FormEvent) => {
    e.preventDefault();
    if (!date || !time || !guests || !name || !phone) {
      alert(t("reservation_validation_error"));
      return;
    }
    console.log("Desktop Reservation Request:", { // Log type changed
      date,
      time,
      guests,
      name,
      phone,
      notes,
    });
    alert(t("reservation_request_sent"));
  };

  const getTodayDate = () => {
    const today = new Date();
    const month = (today.getMonth() + 1).toString().padStart(2, "0");
    const day = today.getDate().toString().padStart(2, "0");
    return `${today.getFullYear()}-${month}-${day}`;
  };

  return (
    <main className={styles.reservationsContainer} aria-labelledby="reservation-heading"> {/* Use desktop class */}
      {/* Removed mobile-specific header, assuming global header in layout.tsx provides navigation */}
      <h1 id="reservation-heading" className={styles.pageTitle}>{t("reservations_title")}</h1>
      <p className={styles.pageDescription}>{t("reservations_description")}</p>

      <form onSubmit={handleSubmitReservation} className={styles.reservationForm}> {/* Use desktop class */}
        <p className={styles.formDescription}>
          {t("reservations_form_intro")}
        </p>

        <div className={styles.formRow}> {/* Use desktop class */}
          <div className={styles.formGroupHalf}> {/* Use desktop class */}
            <label htmlFor="reservationDate">{t("date_label")}</label>
            <input
              type="date"
              id="reservationDate"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              required
              min={getTodayDate()}
              className={styles.inputFieldReservations} /* Use desktop class */
            />
          </div>
          <div className={styles.formGroupHalf}> {/* Use desktop class */}
            <label htmlFor="reservationTime">{t("time_label")}</label>
            <input
              type="time"
              id="reservationTime"
              value={time}
              onChange={(e) => setTime(e.target.value)}
              required
              className={styles.inputFieldReservations} /* Use desktop class */
              min="11:00"
              max="22:00"
            />
          </div>
        </div>

        <div className={styles.formGroup}> {/* Use desktop class */}
          <label htmlFor="reservationGuests">{t("guests_label")}</label>
          <select
            id="reservationGuests"
            value={guests}
            onChange={(e) => setGuests(e.target.value)}
            required
            className={styles.selectFieldReservations} /* Use desktop class */
          >
            {[...Array(9)].map((_, i) => (
              <option key={i + 1} value={i + 1}>
                {i + 1} {t(i + 1 > 1 ? "guest_plural" : "guest_singular")}
              </option>
            ))}
            <option value="10+">{t("guests_10_plus")}</option>
          </select>
        </div>

        <div className={styles.formGroup}> {/* Use desktop class */}
          <label htmlFor="reservationName">{t("full_name_label")}</label>
          <input
            type="text"
            id="reservationName"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            className={styles.inputFieldReservations} /* Use desktop class */
            placeholder={t("full_name_placeholder")}
          />
        </div>

        <div className={styles.formGroup}> {/* Use desktop class */}
          <label htmlFor="reservationPhone">{t("customer_phone_label")}</label>
          <input
            type="tel"
            id="reservationPhone"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            required
            className={styles.inputFieldReservations} /* Use desktop class */
            placeholder={t("customer_phone_placeholder")}
          />
        </div>

        <div className={styles.formGroup}> {/* Use desktop class */}
          <label htmlFor="reservationNotes">{t("special_requests_label")}</label>
          <textarea
            id="reservationNotes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={3}
            className={styles.textareaFieldReservations} /* Use desktop class */
            placeholder={t("special_requests_placeholder")}
          />
        </div>

        <button type="submit" className={styles.submitButtonReservations}> {/* Use desktop class */}
          {t("request_reservation_button")}
        </button>

        <p className={styles.confirmationNote}>
          {t("reservation_confirmation_note")}
        </p>
      </form>
    </main>
  );
}

