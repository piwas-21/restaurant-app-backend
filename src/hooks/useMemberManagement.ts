'use client';

import { useState, useCallback } from 'react';
import { fetchUsers, deleteStaff, updateStaff } from '@/services/userService';
import type { UserDto, UpdateStaffCommand } from '@/types/user';

export const useMemberManagement = () => {
  const [users, setUsers] = useState<any[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const getUsers = useCallback(async (role: string, showDeleted: boolean, searchTerm: string, page: number, pageSize: number) => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await fetchUsers(role, showDeleted, searchTerm, page, pageSize) as { success: boolean; data?: { items: any[]; totalCount: number } };
      if (data.success && data.data) {
        const fetchedUsers = data.data.items;
        const usersToDisplay = role === '' ? fetchedUsers.filter((user: any) => user.role !== 'Customer') : fetchedUsers;
        setUsers(usersToDisplay);
        setTotalCount(data.data.totalCount);
      } else {
        setError('Failed to fetch users');
      }
    } catch {
      setError('An error occurred while fetching users');
    } finally {
      setIsLoading(false);
    }
  }, []);

  const handleDeleteUser = async (userId: string) => {
    try {
      const data = await deleteStaff(userId) as { success: boolean; message?: string };
      return { success: data.success, message: data.message };
    } catch {
      return { success: false, message: 'An unexpected error occurred.' };
    }
  };

  const handleUpdateUser = async (user: UserDto, updates: Partial<UserDto>, newPassword?: string) => {
    try {
      // Only staff/admin users can be updated by admin
      if (user.role === 'Customer') {
        return { success: false, message: 'Customers can only update their own profile' };
      }

      const command: UpdateStaffCommand = {
        userId: user.id,
        firstName: updates.firstName ?? user.firstName,
        lastName: updates.lastName ?? user.lastName,
        email: updates.email ?? user.email,
        phoneNumber: updates.phoneNumber ?? user.phoneNumber,
        role: updates.role ?? user.role,
        password: newPassword,
      };
      const data = await updateStaff(command);
      return { success: data.success, message: data.message };
    } catch {
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
    handleUpdateUser,
  };
};
