/**
 * Order API Type Definitions
 *
 * These types match the backend API DTOs for the Order endpoints.
 * Backend API: http://localhost:5221/api/Orders
 */

/**
 * Order type enum
 */
export enum OrderType {
  DineIn = 'DineIn',
  Takeaway = 'Takeaway',
  Delivery = 'Delivery'
}

/**
 * Payment method enum
 */
export enum PaymentMethod {
  Cash = 'Cash',
  CreditCard = 'CreditCard',
  DebitCard = 'DebitCard',
  OnlinePayment = 'OnlinePayment',
  MobilePayment = 'MobilePayment',
  BankTransfer = 'BankTransfer'
}

/**
 * Order status values
 */
export type OrderStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Preparing'
  | 'Ready'
  | 'InTransit'
  | 'Delivered'
  | 'Completed'
  | 'Cancelled';

/**
 * Payment status values
 */
export type PaymentStatus =
  | 'Pending'
  | 'Paid'
  | 'PartiallyPaid'
  | 'Refunded'
  | 'Failed';

/**
 * Delivery address for orders
 */
export interface CreateOrderDeliveryAddressDto {
  street: string;
  city: string;
  state?: string;
  postalCode: string;
  country: string;
  latitude?: number;
  longitude?: number;
  additionalInfo?: string;
}

export interface DeliveryAddressDto {
  id: string;
  orderId: string;
  userAddressId?: string;
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
  fullAddress: string;
}

/**
 * Order item details
 */
export interface CreateOrderItemDto {
  productId: string;
  productVariationId?: string;
  menuId?: string;
  quantity: number;
  unitPrice: number;
  specialInstructions?: string;
}

export interface OrderItemDto extends CreateOrderItemDto {
  id: string;
  productName?: string;
  productDescription?: string;
  productImageUrl?: string;
  variationName?: string;
  menuName?: string;
  itemTotal: number;
}

/**
 * Payment details for orders
 */
export interface CreateOrderPaymentDto {
  paymentMethod: PaymentMethod;
  amount: number;
  transactionId?: string;
  referenceNumber?: string;
  cardLastFourDigits?: string;
  cardType?: string;
  paymentGateway?: string;
  paymentNotes?: string;
}

export interface OrderPaymentDto extends CreateOrderPaymentDto {
  id: string;
  orderId: string;
  status: PaymentStatus;
  paidAt?: string;
  refundedAt?: string;
  refundAmount?: number;
  refundReason?: string;
}

/**
 * Order status history entry
 */
export interface OrderStatusHistoryDto {
  id: string;
  orderId: string;
  status: OrderStatus;
  changedAt: string;
  changedBy?: string;
  notes?: string;
}

/**
 * Create order command (request)
 */
export interface CreateOrderCommand {
  sessionId?: string;
  userId?: string;
  customerName?: string;
  customerEmail?: string;
  customerPhone?: string;
  type: OrderType;
  tableNumber?: number;
  promoCode?: string;
  hasUserLimitDiscount?: boolean;
  userLimitAmount?: number;
  isFocusOrder?: boolean;
  priority?: number;
  focusReason?: string;
  notes?: string;
  deliveryAddress?: CreateOrderDeliveryAddressDto;
  items?: CreateOrderItemDto[];
  payments?: CreateOrderPaymentDto[];
}

/**
 * Complete order data (response)
 */
export interface OrderDto {
  id: string;
  orderNumber: string;
  userId?: string;
  customerName?: string;
  customerEmail?: string;
  customerPhone?: string;
  type: string;
  tableNumber?: number;
  subTotal: number;
  tax: number;
  deliveryFee: number;
  discount: number;
  discountPercentage: number;
  tip: number;
  total: number;
  totalPaid: number;
  remainingAmount: number;
  isFullyPaid: boolean;
  status: string;
  paymentStatus: string;
  isFocusOrder: boolean;
  priority?: number;
  focusReason?: string;
  focusedAt?: string;
  focusedBy?: string;
  orderDate: string;
  estimatedDeliveryTime?: string;
  actualDeliveryTime?: string;
  createdAt?: string;
  updatedAt?: string;
  notes?: string;
  deliveryAddress?: DeliveryAddressDto;
  cancellationReason?: string;
  promoCode?: string;
  hasUserLimitDiscount: boolean;
  userLimitAmount: number;
  items: OrderItemDto[];
  payments: OrderPaymentDto[];
  statusHistory: OrderStatusHistoryDto[];
}

/**
 * Update order status command
 */
export interface UpdateOrderStatusCommand {
  status: OrderStatus;
  notes?: string;
}

/**
 * Cancel order command
 */
export interface CancelOrderCommand {
  reason: string;
}

/**
 * Toggle focus order command
 */
export interface ToggleFocusOrderCommand {
  isFocusOrder: boolean;
  priority?: number;
  focusReason?: string;
}

/**
 * Add payment to order command
 */
export interface AddPaymentToOrderCommand {
  orderId: string;
  paymentMethod: PaymentMethod;
  amount: number;
  transactionId?: string;
  referenceNumber?: string;
  cardLastFourDigits?: string;
  cardType?: string;
  paymentGateway?: string;
  paymentNotes?: string;
}

/**
 * Refund payment command
 */
export interface RefundPaymentCommand {
  amount: number;
  reason: string;
}

/**
 * Order query filters
 */
export interface OrderQueryFilters {
  status?: OrderStatus;
  paymentStatus?: PaymentStatus;
  orderType?: OrderType;
  startDate?: string;
  endDate?: string;
  userId?: string;
  search?: string;
  isFocusOrder?: boolean;
  orderBy?: string;
  descending?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Focus orders query filters
 */
export interface FocusOrdersQueryFilters {
  activeOnly?: boolean;
  priority?: number;
  orderBy?: string;
}

/**
 * Paged result
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/**
 * Generic API response wrapper
 */
export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

/**
 * Order API response types
 */
export type OrderDtoApiResponse = ApiResponse<OrderDto>;
export type OrderDtoListApiResponse = ApiResponse<OrderDto[]>;
export type OrderDtoPagedResultApiResponse = ApiResponse<PagedResult<OrderDto>>;
export type OrderPaymentDtoApiResponse = ApiResponse<OrderPaymentDto>;
