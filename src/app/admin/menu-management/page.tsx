"use client";

import React, { useState } from 'react';
import Link from 'next/link';
import styles from "../../styles/AdminPage.module.css";

// Mock data - replace with API calls
const initialMenuItems = [
  { id: '1', name_en: 'Sarma Piece (Vegan)', category: 'Starters', price: 1.90, availability: true },
  { id: '2', name_en: 'Adana Kebab', category: 'Main Courses', price: 23.90, availability: true },
  { id: '3', name_en: 'Baklava', category: 'Desserts', price: 9.90, availability: false },
];

interface MenuItem {
  id: string;
  name_en: string;
  category: string;
  price: number;
  availability: boolean;
}

export default function MenuManagementPage() {
  const [menuItems, setMenuItems] = useState<MenuItem[]>(initialMenuItems);
  const [isLoading] = useState(false);
  const [error] = useState('');

  const handleToggleAvailability = (itemId: string) => {
    setMenuItems(prevItems =>
      prevItems.map(item =>
        item.id === itemId ? { ...item, availability: !item.availability } : item
      )
    );
  };

  const handleDeleteItem = (itemId: string) => {
    if (confirm('Are you sure you want to delete this item?')) {
      setMenuItems(prevItems => prevItems.filter(item => item.id !== itemId));
    }
  };

  return (
    <main className={styles.adminContainer}>
      <header className={styles.adminHeader}>
        <h1>Menu Management</h1>
        <Link href="/admin/dashboard" className={styles.adminButton} style={{ backgroundColor: "#6c757d", color: "white", textDecoration: "none" }}>Back to Dashboard</Link>
      </header>
      <section className={styles.adminContent}>
        <button className={`${styles.adminButton} ${styles.add}`}>Add New Menu Item</button>
        {isLoading && <p>Loading menu items...</p>}
        {error && <p className="errorMessage">Error: {error}</p>}
        {!isLoading && !error && (
          <div className={styles.adminTableContainer}>
            <table className={styles.adminTable}>
              <thead>
                <tr>
                  <th>Name (English)</th>
                  <th>Category</th>
                  <th>Price (CHF)</th>
                  <th>Available</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {menuItems.map(item => (
                  <tr key={item.id}>
                    <td>{item.name_en}</td>
                    <td>{item.category}</td>
                    <td>{item.price.toFixed(2)}</td>
                    <td>{item.availability ? 'Yes' : 'No'}</td>
                    <td>
                      <button className={`${styles.adminButton} ${styles.edit}`}>Edit</button>
                      <button
                        onClick={() => handleToggleAvailability(item.id)}
                        className={styles.adminButton}
                        style={{ backgroundColor: item.availability ? "#ffc107" : "#28a745", color: item.availability ? "black" : "white" }}
                      >
                        {item.availability ? 'Set Unavailable' : 'Set Available'}
                      </button>
                      <button onClick={() => handleDeleteItem(item.id)} className={`${styles.adminButton} ${styles.delete}`}>Delete</button>
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
