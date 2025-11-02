import { apiClient } from '@/utils/apiClient';

interface TableQRCodeResponse {
  tableId: string;
  tableNumber: string;
  qrCodeData: string;
  qrCodeGeneratedAt: string;
  qrCodeUrl: string;
}

interface TableValidationResponse {
  isValid: boolean;
  tableId: string;
  tableNumber: string;
  maxGuests: number;
  isOutdoor: boolean;
  qrCodeGeneratedAt?: string;
}

export async function generateTableQRCode(tableId: string): Promise<TableQRCodeResponse> {
  const response = await apiClient.post<{ success: boolean; data: TableQRCodeResponse }>(
    `/api/Tables/${tableId}/generate-qr`
  );
  return response.data;
}

export async function validateTableQRCode(qrCodeData: string): Promise<TableValidationResponse> {
  const response = await apiClient.get<{ success: boolean; data: TableValidationResponse }>(
    `/api/Tables/validate-qr/${encodeURIComponent(qrCodeData)}`
  );
  return response.data;
}
