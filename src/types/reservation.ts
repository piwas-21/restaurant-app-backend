export enum ReservationStatus {
  Pending = 0,
  Confirmed = 1,
  Cancelled = 2,
  Completed = 3,
  NoShow = 4
}

export interface TableDto {
  id: string;
  tableNumber: string;
  maxGuests: number;
  isActive: boolean;
  isOutdoor: boolean;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  shape?: string; // circle, square, rectangle
}

export interface ReservationDto {
  id: string;
  customerId?: string;
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  tableId: string;
  tableNumber: string;
  reservationDate: string;
  startTime: string;
  endTime: string;
  numberOfGuests: number;
  status: ReservationStatus;
  specialRequests?: string;
  notes?: string;
  createdAt: string;
}

export interface CreateReservationDto {
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  tableId: string;
  reservationDate: string;
  startTime: string;
  endTime: string;
  numberOfGuests: number;
  specialRequests?: string;
}

export interface TimeSlotDto {
  startTime: string;
  endTime: string;
  availableTables: TableDto[];
}

export interface AvailableTimeSlotsDto {
  date: string;
  timeSlots: TimeSlotDto[];
}

export interface ReservationFormData {
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  numberOfGuests: number;
  reservationDate: Date;
  selectedTimeSlot?: TimeSlotDto;
  selectedTable?: TableDto;
  specialRequests: string;
}

export interface CreateTableDto {
  tableNumber: string;
  maxGuests: number;
  isActive?: boolean;
  isOutdoor?: boolean;
  positionX?: number;
  positionY?: number;
  width?: number;
  height?: number;
  shape?: string;
}

export interface UpdateTableDto {
  tableNumber: string;
  maxGuests: number;
  isActive: boolean;
  isOutdoor: boolean;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  shape: string;
}

// API Response Types
export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage?: boolean;
  hasPreviousPage?: boolean;
}
