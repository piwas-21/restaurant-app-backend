import { apiClient } from '@/utils/apiClient';
import * as userService from './userService';
import { UserRole } from '@/types/user';

// Mock the apiClient
jest.mock('@/utils/apiClient');

const mockApiClient = apiClient as jest.Mocked<typeof apiClient>;

describe('userService', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('fetchUsers', () => {
    it('should fetch users successfully', async () => {
      const mockResponse = { success: true, data: { items: [{ id: '1', firstName: 'John' }], totalCount: 1 } };
      mockApiClient.get.mockResolvedValue(mockResponse);

      const role = 'Customer';
      const isDeleted = false;
      const search = '';
      const page = 1;
      const pageSize = 10;

      const result = await userService.fetchUsers(role, isDeleted, search, page, pageSize);

      expect(mockApiClient.get).toHaveBeenCalledWith(
        `/api/User/users?Role=Customer&IsDeleted=false&Search=&Page=1&PageSize=10`,
      );
      expect(result).toEqual(mockResponse);
    });

    it('should handle error when fetching users', async () => {
      const mockErrorResponse = { success: false, message: 'Failed to fetch users' };
      mockApiClient.get.mockResolvedValue(mockErrorResponse);

      const result = await userService.fetchUsers('', false, '', 1, 10);

      expect(result).toEqual(mockErrorResponse);
    });
  });

  describe('registerStaff', () => {
    it('should register staff successfully', async () => {
      const mockResponse = { success: true, message: 'Staff registered successfully' };
      mockApiClient.post.mockResolvedValue(mockResponse);

      const staffData = {
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@example.com',
        password: 'Password123!',
        confirmPassword: 'Password123!',
        role: UserRole.Server,
      };
      const result = await userService.registerStaff(staffData);

      expect(mockApiClient.post).toHaveBeenCalledWith('/api/User/register/staff', staffData);
      expect(result).toEqual(mockResponse);
    });

    it('should handle error during staff registration', async () => {
      const mockErrorResponse = { success: false, message: 'Registration failed' };
      mockApiClient.post.mockResolvedValue(mockErrorResponse);

      const staffData = {
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@example.com',
        password: 'Password123!',
        confirmPassword: 'Password123!',
        role: UserRole.Server,
      };
      const result = await userService.registerStaff(staffData);

      expect(result).toEqual(mockErrorResponse);
    });
  });

  describe('delete staff', () => {
    it('should delete user successfully', async () => {
      const mockResponse = { success: true, message: 'User deleted successfully' };
      mockApiClient.delete.mockResolvedValue(mockResponse);

      const userId = 'user123';
      const result = await userService.deleteStaff(userId);

      expect(mockApiClient.delete).toHaveBeenCalledWith('/api/User/delete/user', {
        body: JSON.stringify({ userId, permanent: false }),
      });
      expect(result).toEqual(mockResponse);
    });

    it('should handle error during user deletion', async () => {
      const mockErrorResponse = { success: false, message: 'Deletion failed' };
      mockApiClient.delete.mockResolvedValue(mockErrorResponse);

      const userId = 'user123';
      const result = await userService.deleteStaff(userId);

      expect(result).toEqual(mockErrorResponse);
    });
  });
});
