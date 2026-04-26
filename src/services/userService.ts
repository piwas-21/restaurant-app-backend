import { apiClient } from '@/utils/apiClient';
import type {
  UserDto,
  RegisterStaffCommand,
  UpdateStaffCommand,
  UpdateCustomerCommand,
  UpdateUserDiscountsCommand,
  UserStatistics,
  PagedResult,
  ApiResponse,
  UpdateUserProfileCommand
} from '@/types/user';

// Re-export types for convenience
export type { UpdateUserProfileCommand, UserDto };

const USER_API_URL = `/api/User`;

/**
 * Get current user profile
 */
export async function getCurrentUser(): Promise<UserDto> {
  try {
    const json = await apiClient.get<ApiResponse<UserDto>>(`${USER_API_URL}/profile`);

    if (!json.data) {
      throw new Error('Failed to fetch user profile');
    }

    return json.data;
  } catch (error) {
    // Don't log auth errors - they're expected for non-authenticated users during checkout
    // Only log unexpected errors
    if (error instanceof Error && !error.message.toLowerCase().includes('auth')) {
      // eslint-disable-next-line no-console
      console.error('Error fetching user profile:', error);
    }
    throw error;
  }
}

/**
 * Update current user's profile
 */
export async function updateProfile(command: UpdateUserProfileCommand): Promise<UserDto> {
  try {
    const json = await apiClient.put<ApiResponse<UserDto>>(`${USER_API_URL}/profile`, command);

    if (!json.data) {
      throw new Error('Failed to update profile');
    }

    return json.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error updating profile:', error);
    throw error;
  }
}

/**
 * Fetch users with filters (Admin only)
 */
export const fetchUsers = async (
  role: string,
  isDeleted: boolean,
  search: string,
  page: number,
  pageSize: number
): Promise<ApiResponse<PagedResult<UserDto>>> => {
  const params = new URLSearchParams({
    Role: role,
    IsDeleted: String(isDeleted),
    Search: search,
    Page: String(page),
    PageSize: String(pageSize),
  });

  return await apiClient.get<ApiResponse<PagedResult<UserDto>>>(`${USER_API_URL}/users?${params.toString()}`);
};

/**
 * Register a new staff member (Admin only)
 */
export const registerStaff = async (command: RegisterStaffCommand): Promise<ApiResponse<any>> => {
  return await apiClient.post<ApiResponse<any>>(`${USER_API_URL}/register/staff`, command);
};

/**
 * Update staff member details (Admin only)
 */
export const updateStaff = async (command: UpdateStaffCommand): Promise<ApiResponse<any>> => {
  return await apiClient.post<ApiResponse<any>>(`${USER_API_URL}/update/staff`, command);
};

/**
 * Update customer profile (Admin only)
 */
export const updateCustomer = async (command: UpdateCustomerCommand): Promise<ApiResponse<UserDto>> => {
  return await apiClient.put<ApiResponse<UserDto>>(`${USER_API_URL}/profile`, command);
};

/**
 * Update user discount settings (Admin only)
 */
export const updateUserDiscounts = async (command: UpdateUserDiscountsCommand): Promise<ApiResponse<UserDto>> => {
  return await apiClient.put<ApiResponse<UserDto>>(`${USER_API_URL}/user-discounts`, command);
};

/**
 * Delete/Soft delete a user (Admin only)
 */
export const deleteUser = async (userId: string, permanent: boolean = false): Promise<ApiResponse<string>> => {
  return await apiClient.delete<ApiResponse<string>>(`${USER_API_URL}/delete/user`, {
    body: JSON.stringify({ userId, permanent })
  });
};

/**
 * Reactivate a soft-deleted user (Admin only)
 */
export const reactivateUser = async (userId: string): Promise<ApiResponse<string>> => {
  return await apiClient.post<ApiResponse<string>>(`${USER_API_URL}/reactivate/${userId}`, {});
};

/**
 * Legacy method - kept for backward compatibility
 */
export const deleteStaff = async (userId: string, permanent: boolean = false): Promise<ApiResponse<string>> => {
  return deleteUser(userId, permanent);
};

/**
 * Get user statistics (Admin only)
 */
export const getUserStatistics = async (): Promise<ApiResponse<UserStatistics>> => {
  return await apiClient.get<ApiResponse<UserStatistics>>(`${USER_API_URL}/statistics`);
};
