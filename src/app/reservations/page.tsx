// src/app/reservations/page.tsx
"use client";

import React, { useState, useEffect } from "react"; // Added useEffect
import styles from "../styles/Reservations.module.css"; 
import { useTranslation } from "react-i18next";

export default function ReservationsPage() {
  const { t } = useTranslation();
  const [date, setDate] = useState("");
  const [time, setTime] = useState("");
  const [guests, setGuests] = useState("2");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [isMounted, setIsMounted] = useState(false); // State to track client-side mount

  useEffect(() => {
    setIsMounted(true); // Set to true after component mounts on the client
  }, []);

  const handleSubmitReservation = (e: React.FormEvent) => {
    e.preventDefault();
    if (!date || !time || !guests || !name || !phone) {
      alert(t("reservation_validation_error"));
      return;
    }
    console.log("Desktop Reservation Request:", {
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

  if (!isMounted) {
    // Render nothing or a fallback loader on the server and initial client render
    // This ensures the server and client render match before hydration.
    // You could return a skeleton loader here if preferred.
    return null; 
  }

  return (
    <main className={styles.reservationsContainer} aria-labelledby="reservation-heading">
      <h1 id="reservation-heading" className={styles.pageTitle}>{t("reservations_title")}</h1>
      <p className={styles.pageDescription}>{t("reservations_description")}</p>

      <form onSubmit={handleSubmitReservation} className={styles.reservationForm}>
        <p className={styles.formDescription}>
          {t("reservations_form_intro")}
        </p>

        <div className={styles.formRow}>
          <div className={styles.formGroupHalf}>
            <label htmlFor="reservationDate">{t("date_label")}</label>
            <input
              type="date"
              id="reservationDate"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              required
              min={getTodayDate()}
              className={styles.inputFieldReservations}
            />
          </div>
          <div className={styles.formGroupHalf}>
            <label htmlFor="reservationTime">{t("time_label")}</label>
            <input
              type="time"
              id="reservationTime"
              value={time}
              onChange={(e) => setTime(e.target.value)}
              required
              className={styles.inputFieldReservations}
              min="11:00"
              max="22:00"
            />
          </div>
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="reservationGuests">{t("guests_label")}</label>
          <select
            id="reservationGuests"
            value={guests}
            onChange={(e) => setGuests(e.target.value)}
            required
            className={styles.selectFieldReservations}
          >
            {[...Array(9)].map((_, i) => (
              <option key={i + 1} value={i + 1}>
                {i + 1} {t(i + 1 > 1 ? "guest_plural" : "guest_singular")}
              </option>
            ))}
            <option value="10+">{t("guests_10_plus")}</option>
          </select>
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="reservationName">{t("full_name_label")}</label>
          <input
            type="text"
            id="reservationName"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            className={styles.inputFieldReservations}
            placeholder={t("full_name_placeholder")}
          />
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="reservationPhone">{t("customer_phone_label")}</label>
          <input
            type="tel"
            id="reservationPhone"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            required
            className={styles.inputFieldReservations}
            placeholder={t("customer_phone_placeholder")}
          />
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="reservationNotes">{t("special_requests_label")}</label>
          <textarea
            id="reservationNotes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={3}
            className={styles.textareaFieldReservations}
            placeholder={t("special_requests_placeholder")}
          />
        </div>

        <button type="submit" className={styles.submitButtonReservations}>
          {t("request_reservation_button")}
        </button>

        <p className={styles.confirmationNote}>
          {t("reservation_confirmation_note")}
        </p>
      </form>
    </main>
  );
}
