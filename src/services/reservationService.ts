// Reservation Service - API client for reservation operations
import { apiClient } from '@/utils/apiClient';
import {
  TableDto,
  ReservationDto,
  CreateReservationDto,
  CreateTableDto,
  UpdateTableDto,
  AvailableTimeSlotsDto,
  ReservationStatus,
  ApiResponse,
  PagedResult
} from '@/types/reservation';

// Helper function to map string status from API to enum
function mapStatusToEnum(status: string | ReservationStatus): ReservationStatus {
  if (typeof status === 'number') {
    return status;
  }

  const statusMap: Record<string, ReservationStatus> = {
    'Pending': ReservationStatus.Pending,
    'Confirmed': ReservationStatus.Confirmed,
    'Cancelled': ReservationStatus.Cancelled,
    'Completed': ReservationStatus.Completed,
    'NoShow': ReservationStatus.NoShow,
    'No-Show': ReservationStatus.NoShow,
  };

  return statusMap[status] ?? ReservationStatus.Pending;
}

export const reservationService = {
  // Tables
  async getTables(isActive?: boolean, isOutdoor?: boolean): Promise<TableDto[]> {
    const params = new URLSearchParams();
    if (isActive !== undefined) params.append('isActive', String(isActive));
    if (isOutdoor !== undefined) params.append('isOutdoor', String(isOutdoor));

    const response = await apiClient.get<ApiResponse<TableDto[]>>(`/api/tables?${params}`);
    return response.data || [];
  },

  async getTableById(id: string): Promise<TableDto> {
    const response = await apiClient.get<ApiResponse<TableDto>>(`/api/tables/${id}`);
    if (!response.data) {
      throw new Error('Table not found');
    }
    return response.data;
  },

  async createTable(data: CreateTableDto): Promise<TableDto> {
    const response = await apiClient.post<ApiResponse<TableDto>>('/api/tables', data);
    if (!response.success || !response.data) {
      throw new Error(response.message || 'Failed to create table');
    }
    return response.data;
  },

  async updateTable(id: string, data: UpdateTableDto): Promise<TableDto> {
    const response = await apiClient.put<ApiResponse<TableDto>>(`/api/tables/${id}`, data);
    if (!response.success || !response.data) {
      throw new Error(response.message || 'Failed to update table');
    }
    return response.data;
  },

  async deleteTable(id: string): Promise<void> {
    const response = await apiClient.delete<ApiResponse<boolean>>(`/api/tables/${id}`);
    if (!response.success) {
      throw new Error(response.message || 'Failed to delete table');
    }
  },

  // Reservations
  async getReservations(params?: {
    date?: string;
    tableId?: string;
    status?: ReservationStatus;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<ReservationDto>> {
    const queryParams = new URLSearchParams();
    if (params?.date) queryParams.append('date', params.date);
    if (params?.tableId) queryParams.append('tableId', params.tableId);
    if (params?.status !== undefined) queryParams.append('status', String(params.status));
    // Backend expects lowercase 'page' and 'pageSize'
    if (params?.page) queryParams.append('page', String(params.page));
    if (params?.pageSize) queryParams.append('pageSize', String(params.pageSize));

    const response = await apiClient.get<ApiResponse<PagedResult<any>>>(
      `/api/reservations?${queryParams}`
    );

    // Map string status to enum values
    const data = response.data;
    if (data && data.items) {
      data.items = data.items.map((item: any) => ({
        ...item,
        status: mapStatusToEnum(item.status)
      }));
    }

    return data || {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
      totalPages: 0
    };
  },

  async getAvailableTimeSlots(date: string, numberOfGuests: number): Promise<{
    data: AvailableTimeSlotsDto | null;
    error?: string;
    isCapacityIssue?: boolean;
  }> {
    const params = new URLSearchParams({
      date: date,
      numberOfGuests: String(numberOfGuests)
    });

    try {
      const response = await apiClient.get<ApiResponse<AvailableTimeSlotsDto>>(
        `/api/reservations/available-slots?${params}`
      );

      if (!response.success || !response.data) {
        // Check errors array first, then message, then fallback
        const errorMessage = response.errors && response.errors.length > 0
          ? response.errors[0]
          : response.message || 'Failed to fetch available time slots';

        // Check if it's a capacity issue (expected scenario)
        const isCapacityIssue = errorMessage.toLowerCase().includes('no tables available for');

        return {
          data: null,
          error: errorMessage,
          isCapacityIssue
        };
      }

      return { data: response.data };
    } catch (error: any) {
      // Network or unexpected errors
      throw new Error(error.message || 'Failed to fetch available time slots');
    }
  },

  async createReservation(data: CreateReservationDto): Promise<ReservationDto> {
    const response = await apiClient.post<ApiResponse<any>>('/api/reservations', data);

    if (!response.success || !response.data) {
      throw new Error(response.message || 'Failed to create reservation');
    }

    // Map string status to enum
    return {
      ...response.data,
      status: mapStatusToEnum(response.data.status)
    };
  },

  async getReservationById(id: string): Promise<ReservationDto> {
    const response = await apiClient.get<ApiResponse<any>>(`/api/reservations/${id}`);
    if (!response.data) {
      throw new Error('Reservation not found');
    }

    // Map string status to enum
    return {
      ...response.data,
      status: mapStatusToEnum(response.data.status)
    };
  },

  async cancelReservation(id: string): Promise<void> {
    const response = await apiClient.post<ApiResponse<boolean>>(`/api/reservations/${id}/cancel`);

    if (!response.success) {
      throw new Error(response.message || 'Failed to cancel reservation');
    }
  },

  async confirmReservation(id: string): Promise<void> {
    const response = await apiClient.post<ApiResponse<boolean>>(`/api/reservations/${id}/confirm`);

    if (!response.success) {
      throw new Error(response.message || 'Failed to confirm reservation');
    }
  },

  async updateReservationStatus(id: string, status: ReservationStatus): Promise<void> {
    const response = await apiClient.put<ApiResponse<boolean>>(`/api/reservations/${id}/status`, { status });

    if (!response.success) {
      throw new Error(response.message || 'Failed to update reservation status');
    }
  },

  async deleteReservation(id: string): Promise<void> {
    const response = await apiClient.delete<ApiResponse<boolean>>(`/api/reservations/${id}`);

    if (!response.success) {
      throw new Error(response.message || 'Failed to delete reservation');
    }
  },

  // Helper functions
  formatTimeSlot(startTime: string, endTime: string): string {
    // Convert "HH:mm:ss" format to "HH:mm"
    const formatTime = (time: string) => {
      const parts = time.split(':');
      return `${parts[0]}:${parts[1]}`;
    };

    return `${formatTime(startTime)} - ${formatTime(endTime)}`;
  },

  getStatusLabel(status: ReservationStatus): string {
    const labels: Record<ReservationStatus, string> = {
      [ReservationStatus.Pending]: 'Pending',
      [ReservationStatus.Confirmed]: 'Confirmed',
      [ReservationStatus.Cancelled]: 'Cancelled',
      [ReservationStatus.Completed]: 'Completed',
      [ReservationStatus.NoShow]: 'No Show'
    };
    return labels[status] || 'Unknown';
  },

  getStatusColor(status: ReservationStatus): string {
    const colors: Record<ReservationStatus, string> = {
      [ReservationStatus.Pending]: '#f59e0b',
      [ReservationStatus.Confirmed]: '#10b981',
      [ReservationStatus.Cancelled]: '#ef4444',
      [ReservationStatus.Completed]: '#6b7280',
      [ReservationStatus.NoShow]: '#ef4444'
    };
    return colors[status];
  }
};
