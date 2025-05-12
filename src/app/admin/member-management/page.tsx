// src/app/admin/member-management/page.tsx
"use client";

import React, { useState } from "react";
import Link from "next/link";
import styles from "../../styles/AdminPage.module.css";

// Mock data - replace with API calls
const initialMembers = [
  { id: "m1", firstName: "John", lastName: "Doe", email: "john.doe@example.com", loyalty_points: 150 },
  { id: "m2", firstName: "Jane", lastName: "Smith", email: "jane.smith@example.com", loyalty_points: 275 },
];

interface Member {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  loyalty_points: number;
  // Add other fields like registration date, etc.
}

export default function MemberManagementPage() {
  const [members, setMembers] = useState<Member[]>(initialMembers);
  const [isLoading] = useState(false);
  const [error] = useState("");

  // TODO: useEffect to fetch members from API

  const handleEditMember = (memberId: string) => {
    // For now, just log. In a real app, this would open an edit form/modal.
    console.log("Edit member:", memberId);
    alert(`Editing member ${memberId} - functionality to be implemented.`);
  };

  const handleDeleteMember = (memberId: string) => {
    if (confirm("Are you sure you want to delete this member?")) {
      setMembers(prevMembers => prevMembers.filter(m => m.id !== memberId));
      // TODO: API call to delete member
    }
  };

  return (
    <main className={styles.adminContainer}>
      <header className={styles.adminHeader}>
        <h1>Manage Members</h1>
        <Link href="/admin/dashboard" className={styles.adminButton} style={{backgroundColor: "#6c757d", color: "white", textDecoration: "none"}}>Back to Dashboard</Link>
      </header>
      <section className={styles.adminContent}>
        {/* Add New Member button could be here if manual addition is a feature */}
        {isLoading && <p>Loading members...</p>}
        {error && <p className="errorMessage">Error: {error}</p>}
        {!isLoading && !error && (
          <table className={styles.adminTable}>
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Loyalty Points</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {members.map(member => (
                <tr key={member.id}>
                  <td>{member.firstName} {member.lastName}</td>
                  <td>{member.email}</td>
                  <td>{member.loyalty_points}</td>
                  <td>
                    <button onClick={() => handleEditMember(member.id)} className={`${styles.adminButton} ${styles.edit}`}>Edit</button>
                    <button onClick={() => handleDeleteMember(member.id)} className={`${styles.adminButton} ${styles.delete}`}>Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}

