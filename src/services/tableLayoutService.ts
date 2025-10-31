import { apiClient } from '@/utils/apiClient';
import type { TableDto, UpdateTableDto, CreateTableDto, ApiResponse } from '@/types/reservation';

class TableLayoutService {
  /**
   * Get all tables for layout editor
   */
  async getAllTables(): Promise<TableDto[]> {
    const response = await apiClient.get<ApiResponse<TableDto[]>>('/api/tables');
    return response.data || [];
  }

  /**
   * Update table position and properties
   */
  async updateTable(id: string, data: UpdateTableDto): Promise<TableDto> {
    const response = await apiClient.put<ApiResponse<TableDto>>(`/api/tables/${id}`, data);
    if (!response.success || !response.data) {
      throw new Error(response.message || 'Failed to update table');
    }
    return response.data;
  }

  /**
   * Batch update multiple tables (for layout changes)
   */
  async batchUpdateTables(tables: Array<{ id: string; data: UpdateTableDto }>): Promise<void> {
    // Execute all updates in parallel
    await Promise.all(
      tables.map(({ id, data }) => this.updateTable(id, data))
    );
  }

  /**
   * Create a new table
   */
  async createTable(data: CreateTableDto): Promise<TableDto> {
    const response = await apiClient.post<ApiResponse<TableDto>>('/api/tables', data);
    if (!response.success || !response.data) {
      throw new Error(response.message || 'Failed to create table');
    }
    return response.data;
  }

  /**
   * Delete a table
   */
  async deleteTable(id: string): Promise<void> {
    const response = await apiClient.delete<ApiResponse<void>>(`/api/tables/${id}`);
    if (!response.success) {
      throw new Error(response.message || 'Failed to delete table');
    }
  }
}

export const tableLayoutService = new TableLayoutService();
export default tableLayoutService;
