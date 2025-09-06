"use client";

import React from 'react';
import Link from 'next/link';
import styles from "../../styles/AdminPage.module.css";

export default function AdminDashboardPage() {
  return (
    <main className={styles.adminContainer}>
      <header className={styles.adminHeader}>
        <h1>Admin Dashboard</h1>
      </header>
      <nav className={styles.adminNav}>
        <ul>
          <li><Link href="/admin/menu-management">Manage Menu</Link></li>
          <li><Link href="/admin/specials-management">Manage Daily Specials</Link></li>
          <li><Link href="/admin/member-management">Manage Members</Link></li>
          <li><Link href="/admin/category-management">Manage Categories</Link></li>
        </ul>
      </nav>
      <section className={styles.adminContent}>
        <h2>Welcome, Admin!</h2>
        <p>Select an option from the navigation to manage restaurant data.</p>
      </section>
    </main>
  );
}
