import { apiClient } from '@/utils/apiClient';
import { fidelityPointsService } from '@/services/fidelityPointsService';

// Mock the apiClient
jest.mock('@/utils/apiClient', () => ({
  apiClient: {
    get: jest.fn(),
    post: jest.fn(),
  },
}));

describe('fidelityPointsService', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('getBalance', () => {
    it('should return user balance', async () => {
      const mockBalance = {
        userId: '123',
        currentPoints: 500,
        totalEarnedPoints: 1000,
        totalRedeemedPoints: 500,
        lastUpdated: new Date().toISOString(),
      };

      (apiClient.get as jest.Mock).mockResolvedValueOnce({ data: mockBalance });

      const result = await fidelityPointsService.getBalance();

      expect(apiClient.get).toHaveBeenCalledWith('/api/FidelityPoints/balance', { requireAuth: true });
      expect(result).toEqual(mockBalance);
    });

    it('should handle errors gracefully', async () => {
      (apiClient.get as jest.Mock).mockRejectedValueOnce(new Error('API Error'));

      await expect(fidelityPointsService.getBalance()).rejects.toThrow();
    });
  });

  describe('getHistory', () => {
    it('should return paginated transaction history', async () => {
      const mockHistory = {
        items: [
          {
            id: '1',
            userId: '123',
            transactionType: 'Earned',
            points: 100,
            description: 'Order #1',
            createdAt: new Date().toISOString(),
          },
        ],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        hasNextPage: false,
      };

      (apiClient.get as jest.Mock).mockResolvedValueOnce({ data: mockHistory.items });

      const result = await fidelityPointsService.getHistory({ page: 1, pageSize: 20 });

      expect(apiClient.get).toHaveBeenCalled();
      expect(result).toEqual(mockHistory.items);
    });
  });

  describe('calculateDiscount', () => {
    it('should calculate discount from points', async () => {
      (apiClient.get as jest.Mock).mockResolvedValueOnce({ data: 5.0 });

      const result = await fidelityPointsService.calculateDiscount(500);

      expect(apiClient.get).toHaveBeenCalled();
      expect(result).toBe(5.0);
    });
  });

  describe('calculatePoints', () => {
    it('should calculate points needed for discount', async () => {
      (apiClient.get as jest.Mock).mockResolvedValueOnce({ data: 500 });

      const result = await fidelityPointsService.calculatePoints(5.0);

      expect(apiClient.get).toHaveBeenCalled();
      expect(result).toBe(500);
    });
  });

  describe('formatPointsAsCurrency', () => {
    it('should format points as currency', () => {
      expect(fidelityPointsService.formatPointsAsCurrency(100)).toBe('$1.00');
      expect(fidelityPointsService.formatPointsAsCurrency(500)).toBe('$5.00');
      expect(fidelityPointsService.formatPointsAsCurrency(1000)).toBe('$10.00');
    });
  });

  describe('getTransactionTypeLabel', () => {
    it('should return correct labels for transaction types', () => {
      expect(fidelityPointsService.getTransactionTypeLabel('Earned')).toBe('Points Earned');
      expect(fidelityPointsService.getTransactionTypeLabel('Redeemed')).toBe('Points Redeemed');
      expect(fidelityPointsService.getTransactionTypeLabel('AdminAdjustment')).toBe('Admin Adjustment');
      expect(fidelityPointsService.getTransactionTypeLabel('Expired')).toBe('Points Expired');
      expect(fidelityPointsService.getTransactionTypeLabel('Unknown')).toBe('Unknown');
    });
  });

  describe('getTransactionTypeColor', () => {
    it('should return correct colors for transaction types', () => {
      expect(fidelityPointsService.getTransactionTypeColor('Earned')).toBe('text-green-600');
      expect(fidelityPointsService.getTransactionTypeColor('Redeemed')).toBe('text-blue-600');
      expect(fidelityPointsService.getTransactionTypeColor('AdminAdjustment')).toBe('text-purple-600');
      expect(fidelityPointsService.getTransactionTypeColor('Expired')).toBe('text-gray-600');
      expect(fidelityPointsService.getTransactionTypeColor('Unknown')).toBe('text-gray-600');
    });
  });
});
