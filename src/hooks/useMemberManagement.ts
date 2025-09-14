'use client';

import { useState, useEffect, useCallback } from 'react';
import { fetchUsers, deleteStaff } from '@/services/userService';

export const useMemberManagement = () => {
  const [users, setUsers] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const getUsers = useCallback(async (role: string, showDeleted: boolean, searchTerm: string, page: number, pageSize: number) => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await fetchUsers(role, showDeleted, searchTerm, page, pageSize);
      if (data.success) {
        const fetchedUsers = data.data.items;
        const usersToDisplay = role === '' ? fetchedUsers.filter((user: any) => user.role !== 'Customer') : fetchedUsers;
        setUsers(usersToDisplay);
        setTotalCount(data.data.totalCount);
      } else {
        setError('Failed to fetch users');
      }
    } catch (error) {
      setError('An error occurred while fetching users');
    } finally {
      setIsLoading(false);
    }
  }, []);

  const handleDeleteUser = async (userId: string) => {
    try {
      const data = await deleteStaff(userId);
      return { success: data.success, message: data.message };
    } catch (error) {
      return { success: false, message: 'An unexpected error occurred.' };
    }
  };

  return {
    users,
    totalCount,
    isLoading,
    error,
    getUsers,
    handleDeleteUser,
  };
};
