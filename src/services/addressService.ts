/**
 * Address Service
 *
 * Service layer for interacting with the Address API.
 * Handles CRUD operations for user delivery addresses.
 */

import { apiClient } from '@/utils/apiClient';

/**
 * Address DTO matching backend AddressDto
 */
export interface AddressDto {
  id: string;
  userId: string;
  label: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  state?: string;
  postalCode: string;
  country: string;
  phone?: string;
  isDefault: boolean;
  latitude?: number;
  longitude?: number;
  deliveryInstructions?: string;
  createdAt: string;
  updatedAt?: string;
}

/**
 * Create Address Command
 */
export interface CreateAddressCommand {
  label: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  state?: string;
  postalCode: string;
  country: string;
  phone?: string;
  latitude?: number;
  longitude?: number;
  deliveryInstructions?: string;
}

/**
 * Update Address Command
 */
export interface UpdateAddressCommand {
  id: string;
  label: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  state?: string;
  postalCode: string;
  country: string;
  phone?: string;
  latitude?: number;
  longitude?: number;
  deliveryInstructions?: string;
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
 * Get all addresses for the current user
 *
 * @returns List of user addresses
 */
export async function getMyAddresses(): Promise<AddressDto[]> {
  try {
    const response = await apiClient.get<ApiResponse<AddressDto[]>>('/api/Addresses', { requireAuth: true });

    if (!response.data) {
      throw new Error('Failed to fetch addresses');
    }

    return response.data;
  } catch (error) {
    // Don't log auth errors - they're expected for non-authenticated users during checkout
    // Only log unexpected errors
    if (error instanceof Error && !error.message.toLowerCase().includes('auth')) {
      console.error('Error fetching addresses:', error);
    }
    throw error;
  }
}

/**
 * Get address by ID
 *
 * @param id - Address ID
 * @returns Address details
 */
export async function getAddressById(id: string): Promise<AddressDto> {
  try {
    const response = await apiClient.get<ApiResponse<AddressDto>>(`/api/Addresses/${id}`, { requireAuth: true });

    if (!response.data) {
      throw new Error('Failed to fetch address');
    }

    return response.data;
  } catch (error) {
    console.error('Error fetching address:', error);
    throw error;
  }
}

/**
 * Create a new address
 *
 * @param command - Address creation details
 * @returns Created address
 */
export async function createAddress(command: CreateAddressCommand): Promise<AddressDto> {
  try {
    const response = await apiClient.post<ApiResponse<AddressDto>>('/api/Addresses', command, { requireAuth: true });

    if (!response.data) {
      throw new Error('Failed to create address');
    }

    return response.data;
  } catch (error) {
    console.error('Error creating address:', error);
    throw error;
  }
}

/**
 * Update an existing address
 *
 * @param id - Address ID
 * @param command - Address update details
 * @returns Updated address
 */
export async function updateAddress(id: string, command: UpdateAddressCommand): Promise<AddressDto> {
  try {
    const response = await apiClient.put<ApiResponse<AddressDto>>(`/api/Addresses/${id}`, command, {
      requireAuth: true,
    });

    if (!response.data) {
      throw new Error('Failed to update address');
    }

    return response.data;
  } catch (error) {
    console.error('Error updating address:', error);
    throw error;
  }
}

/**
 * Delete an address
 *
 * @param id - Address ID
 */
export async function deleteAddress(id: string): Promise<void> {
  try {
    await apiClient.delete<ApiResponse<string>>(`/api/Addresses/${id}`, { requireAuth: true });
  } catch (error) {
    console.error('Error deleting address:', error);
    throw error;
  }
}

/**
 * Set an address as default
 *
 * @param id - Address ID
 */
export async function setDefaultAddress(id: string): Promise<void> {
  try {
    await apiClient.post<ApiResponse<string>>(`/api/Addresses/${id}/set-default`, {}, { requireAuth: true });
  } catch (error) {
    console.error('Error setting default address:', error);
    throw error;
  }
}

/**
 * Address service object with all methods
 */
export const addressService = {
  getMyAddresses,
  getAddressById,
  createAddress,
  updateAddress,
  deleteAddress,
  setDefaultAddress,
};
