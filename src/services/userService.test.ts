import { apiClient } from './apiClient';
import * as userService from './userService';

// Mock the apiClient
jest.mock('./apiClient', () => ({
  apiClient: {
    get: jest.fn(),
    post: jest.fn(),
    delete: jest.fn(),
  },
}));

describe('userService', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('fetchUsers', () => {
    it('should fetch users successfully', async () => {
      const mockResponse = { success: true, data: { items: [{ id: '1', firstName: 'John' }], totalCount: 1 } };
      (apiClient.get as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockResponse),
      });

      const role = 'Customer';
      const isDeleted = false;
      const search = '';
      const page = 1;
      const pageSize = 10;

      const result = await userService.fetchUsers(role, isDeleted, search, page, pageSize);

      expect(apiClient.get).toHaveBeenCalledWith(
        `/api/User/users?Role=Customer&IsDeleted=false&Search=&Page=1&PageSize=10`
      );
      expect(result).toEqual(mockResponse);
    });

    it('should handle error when fetching users', async () => {
      const mockErrorResponse = { success: false, message: 'Failed to fetch users' };
      (apiClient.get as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockErrorResponse),
      });

      const result = await userService.fetchUsers('', false, '', 1, 10);

      expect(result).toEqual(mockErrorResponse);
    });
  });

  describe('registerStaff', () => {
    it('should register staff successfully', async () => {
      const mockResponse = { success: true, message: 'Staff registered successfully' };
      (apiClient.post as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockResponse),
      });

      const staffData = { firstName: 'Jane', lastName: 'Doe', email: 'jane.doe@example.com', password: 'Password123!', confirmPassword: 'Password123!', role: 'Server' };
      const result = await userService.registerStaff(staffData);

      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/User/register/staff',
        staffData
      );
      expect(result).toEqual(mockResponse);
    });

    it('should handle error during staff registration', async () => {
      const mockErrorResponse = { success: false, message: 'Registration failed' };
      (apiClient.post as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockErrorResponse),
      });

      const staffData = { firstName: 'Jane', lastName: 'Doe', email: 'jane.doe@example.com', password: 'Password123!', confirmPassword: 'Password123!', role: 'Server' };
      const result = await userService.registerStaff(staffData);

      expect(result).toEqual(mockErrorResponse);
    });
  });

  describe('delete staff', () => {
    it('should delete user successfully', async () => {
      const mockResponse = { success: true, message: 'User deleted successfully' };
      (apiClient.delete as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockResponse),
      });

      const userId = 'user123';
      const result = await userService.deleteStaff(userId);

      expect(apiClient.delete).toHaveBeenCalledWith(
        '/api/User/delete/user',
        { userId }
      );
      expect(result).toEqual(mockResponse);
    });

    it('should handle error during user deletion', async () => {
      const mockErrorResponse = { success: false, message: 'Deletion failed' };
      (apiClient.delete as jest.Mock).mockResolvedValue({
        json: () => Promise.resolve(mockErrorResponse),
      });

      const userId = 'user123';
      const result = await userService.deleteStaff(userId);

      expect(result).toEqual(mockErrorResponse);
    });
  });
});
