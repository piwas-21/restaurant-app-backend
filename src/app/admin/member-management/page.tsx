"use client";

import React, { useState } from "react";
import Link from "next/link";
import styles from "../../styles/AdminPage.module.css";
import { registerStaff, staffRegistrationSchema } from "../../../lib/auth/utils";
import { z } from "zod";

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
}

export default function MemberManagementPage() {
  const [members, setMembers] = useState<Member[]>(initialMembers);
  const [isLoading] = useState(false);
  const [error] = useState("");
  const [showRegistrationForm, setShowRegistrationForm] = useState(false);
  const [registrationError, setRegistrationError] = useState<string | null>(null);
  const [registrationSuccess, setRegistrationSuccess] = useState<string | null>(null);

  // Form state
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [role, setRole] = useState("cashier");

  const handleEditMember = (memberId: string) => {
    console.log("Edit member:", memberId);
    alert(`Editing member ${memberId} - functionality to be implemented.`);
  };

  const handleDeleteMember = (memberId: string) => {
    if (confirm("Are you sure you want to delete this member?")) {
      setMembers(prevMembers => prevMembers.filter(m => m.id !== memberId));
    }
  };

  const handleRegistrationSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setRegistrationError(null);
    setRegistrationSuccess(null);

    const user = JSON.parse(localStorage.getItem('user') || '{}');
    const token = user.accessToken;

    if (!token) {
      setRegistrationError("Unauthorized. Please log in again.");
      return;
    }

    try {
      const formData = { firstName, lastName, email, password, confirmPassword, role };
      staffRegistrationSchema.parse(formData);

      const response = await registerStaff(formData, token);

      if (response.success) {
        setRegistrationSuccess(response.message);
        setFirstName("");
        setLastName("");
        setEmail("");
        setPassword("");
        setConfirmPassword("");
        setRole("cashier");
        setShowRegistrationForm(false);
      } else {
        setRegistrationError(response.message || "Failed to register staff.");
      }
    } catch (error) {
      if (error instanceof z.ZodError) {
        setRegistrationError(error.errors.map(e => e.message).join(", "));
      } else {
        setRegistrationError("An unknown error occurred.");
      }
    }
  };


  return (
    <main className={styles.adminContainer}>
      <header className={styles.adminHeader}>
        <h1>Manage Members</h1>
        <div>
        <button onClick={() => setShowRegistrationForm(!showRegistrationForm)} className={styles.adminButton}>
            {showRegistrationForm ? "Cancel" : "Register New Staff"}
          </button>
        <Link href="/admin/dashboard" className={styles.adminButton} style={{ backgroundColor: "#6c757d", color: "white", textDecoration: "none" }}>Back to Dashboard</Link>
        </div>
      </header>

      {showRegistrationForm && (
        <section className={styles.adminContent}>
          <h2>Register New Staff</h2>
          <form onSubmit={handleRegistrationSubmit}>
            {registrationError && <p className="errorMessage">{registrationError}</p>}
            {registrationSuccess && <p className="successMessage">{registrationSuccess}</p>}
            <div className={styles.formGroup}>
              <label htmlFor="firstName">First Name</label>
              <input type="text" id="firstName" value={firstName} onChange={e => setFirstName(e.target.value)} required />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="lastName">Last Name</label>
              <input type="text" id="lastName" value={lastName} onChange={e => setLastName(e.target.value)} required />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="email">Email</label>
              <input type="email" id="email" value={email} onChange={e => setEmail(e.target.value)} required />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="password">Password</label>
              <input type="password" id="password" value={password} onChange={e => setPassword(e.target.value)} required />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="confirmPassword">Confirm Password</label>
              <input type="password" id="confirmPassword" value={confirmPassword} onChange={e => setConfirmPassword(e.target.value)} required />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="role">Role</label>
              <select id="role" value={role} onChange={e => setRole(e.target.value)}>
                <option value="cashier">Cashier</option>
                <option value="kitchen-staff">Kitchen Staff</option>
                <option value="server">Server</option>
              </select>
            </div>
            <button type="submit" className={styles.adminButton}>Register</button>
          </form>
        </section>
      )}

      <section className={styles.adminContent}>
        {isLoading && <p>Loading members...</p>}
        {error && <p className="errorMessage">Error: {error}</p>}
        {!isLoading && !error && (
          <div className={styles.adminTableContainer}>
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
          </div>
        )}
      </section>
    </main>
  );
}
