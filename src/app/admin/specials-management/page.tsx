"use client";

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from "@/app/styles/AdminPage.module.css";
import PageHeader from '@/components/admin/PageHeader';

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
  const { t } = useTranslation();
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
      <PageHeader title={t('admin_specials_management_title')}>
        <button className={`${styles.adminButton} ${styles.add}`}>Add New Special</button>
      </PageHeader>
      <section className={styles.adminContent}>
        {isLoading && <p>Loading specials...</p>}
        {error && <p className="errorMessage">{t('error')}: {error}</p>}
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
