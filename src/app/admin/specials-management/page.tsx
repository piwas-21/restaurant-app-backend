"use client";

import React, { useState } from 'react';
import Link from 'next/link';
import styles from "../../styles/AdminPage.module.css";

// Mock data - replace with API calls
const initialSpecials = [
  { id: 's1', name_en: 'Lunch Special: Lamb Doner + Ayran', price: 15.00, date_active: '2025-05-12' },
  { id: 's2', name_en: 'Weekend Pide Feast', price: 29.90, date_active: '2025-05-17' },
];

interface DailySpecial {
  id: string;
  name_en: string;
  price: number;
  date_active: string;
}

export default function SpecialsManagementPage() {
  const [specials, setSpecials] = useState<DailySpecial[]>(initialSpecials);
  const [isLoading] = useState(false);
  const [error] = useState('');

  const handleDeleteSpecial = (specialId: string) => {
    if (confirm('Are you sure you want to delete this special?')) {
      setSpecials(prevSpecials => prevSpecials.filter(s => s.id !== specialId));
    }
  };

  return (
    <main className={styles.adminContainer}>
      <header className={styles.adminHeader}>
        <h1>Manage Daily Specials</h1>
        <Link href="/admin/dashboard" className={styles.adminButton} style={{ backgroundColor: "#6c757d", color: "white", textDecoration: "none" }}>Back to Dashboard</Link>
      </header>
      <section className={styles.adminContent}>
        <button className={`${styles.adminButton} ${styles.add}`}>Add New Special</button>
        {isLoading && <p>Loading specials...</p>}
        {error && <p className="errorMessage">Error: {error}</p>}
        {!isLoading && !error && (
          <div className={styles.adminTableContainer}>
            <table className={styles.adminTable}>
              <thead>
                <tr>
                  <th>Name (English)</th>
                  <th>Price (CHF)</th>
                  <th>Active Date</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {specials.map(special => (
                  <tr key={special.id}>
                    <td>{special.name_en}</td>
                    <td>{special.price.toFixed(2)}</td>
                    <td>{special.date_active}</td>
                    <td>
                      <button className={`${styles.adminButton} ${styles.edit}`}>Edit</button>
                      <button onClick={() => handleDeleteSpecial(special.id)} className={`${styles.adminButton} ${styles.delete}`}>Delete</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  );
}
