/**
 * User role enum matching backend
 */
export enum UserRole {
  Customer = 'Customer',
  Staff = 'Staff',
  Admin = 'Admin'
}

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
  role: UserRole;
  isEmailConfirmed: boolean;
  createdAt: string;
  updatedAt?: string;
  deletedAt?: string;
  isDeleted: boolean;
  metadata: Record<string, string>;
  orderLimitAmount: number;
  discountPercentage: number;
  isDiscountActive: boolean;
}

/**
 * Register staff command
 */
export interface RegisterStaffCommand {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: UserRole;
}

/**
 * Update staff command
 */
export interface UpdateStaffCommand {
  userId: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber?: string;
  role: UserRole;
  password?: string; // Optional - only if changing password
}

/**
 * Update customer profile
 */
export interface UpdateCustomerCommand {
  userId: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
}

/**
 * Update User Profile Command (for current user)
 */
export interface UpdateUserProfileCommand {
  firstName: string;
  lastName: string;
  phoneNumber?: string;
}

/**
 * Update user discount settings
 */
export interface UpdateUserDiscountsCommand {
  userId: string;
  orderLimitAmount: number;
  discountPercentage: number;
  isDiscountActive: boolean;
}

/**
 * User statistics
 */
export interface UserStatistics {
  totalCustomers: number;
  totalStaff: number;
  totalAdmins: number;
  deletedUsers: number;
  recentRegistrations: number; // Last 7 days
  activeDiscounts: number;
}

/**
 * Paged result wrapper
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/**
 * API Response wrapper
 */
export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
  errors?: string[];
}
