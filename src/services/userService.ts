import { apiClient } from './apiClient';

const USER_API_URL = `/api/User`;

/**
 * User DTO matching backend UserDto
 */
export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string;
  role: string;
  isEmailConfirmed: boolean;
  createdAt: string;
  updatedAt?: string;
  metadata: Record<string, string>;
  orderLimitAmount: number;
  discountPercentage: number;
  isDiscountActive: boolean;
}

/**
 * Update User Profile Command
 */
export interface UpdateUserProfileCommand {
  firstName: string;
  lastName: string;
  phoneNumber?: string;
}

/**
 * API Response wrapper
 */
interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

/**
 * Get current user profile
 *
 * @returns Current user details
 */
export async function getCurrentUser(): Promise<UserDto> {
  try {
    const response = await apiClient.get(`${USER_API_URL}/profile`);
    const json = await response.json() as ApiResponse<UserDto>;

    if (!json.data) {
      throw new Error('Failed to fetch user profile');
    }

    return json.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching user profile:', error);
    throw error;
  }
}

/**
 * Update current user's profile
 *
 * @param command - Profile update details
 * @returns Updated user profile
 */
export async function updateProfile(command: UpdateUserProfileCommand): Promise<UserDto> {
  try {
    const response = await apiClient.put(`${USER_API_URL}/profile`, command);
    const json = await response.json() as ApiResponse<UserDto>;

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

export const fetchUsers = async (
  role: string,
  isDeleted: boolean,
  search: string,
  page: number,
  pageSize: number
) => {
  const params = new URLSearchParams({
    Role: role,
    IsDeleted: String(isDeleted),
    Search: search,
    Page: String(page),
    PageSize: String(pageSize),
  });

  const response = await apiClient.get(`${USER_API_URL}/users?${params.toString()}`);
  return response.json();
};

export const registerStaff = async (staffData: any) => {
  const response = await apiClient.post(`${USER_API_URL}/register/staff`, staffData);
  return response.json();
};

export const deleteStaff = async (userId: string) => {
  const response = await apiClient.delete(`${USER_API_URL}/delete/user/${userId}`);
  return response.json();
};
