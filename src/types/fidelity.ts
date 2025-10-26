// Fidelity Points & Customer Discount Types

export interface FidelityPointBalance {
  id: string;
  userId: string;
  currentPoints: number;
  totalEarnedPoints: number;
  totalRedeemedPoints: number;
  lastUpdated: string;
  currentPointsValue: number; // Calculated property: points converted to currency
}

export enum TransactionType {
  Earned = 'Earned',
  Redeemed = 'Redeemed',
  AdminAdjustment = 'AdminAdjustment',
  Expired = 'Expired',
  Refunded = 'Refunded',
}

export interface FidelityPointsTransaction {
  id: string;
  userId: string;
  orderId?: string;
  transactionType: TransactionType;
  points: number;
  orderTotal?: number;
  description: string;
  createdAt: string;
  expiresAt?: string;
}

export interface PointEarningRule {
  id: string;
  name: string;
  minOrderAmount: number;
  maxOrderAmount?: number;
  pointsAwarded: number;
  isActive: boolean;
  priority: number;
  createdAt: string;
  updatedAt?: string;
}

export enum DiscountType {
  Percentage = 'Percentage',
  FixedAmount = 'FixedAmount',
}

export interface CustomerDiscountRule {
  id: string;
  userId: string;
  userEmail: string;
  userName: string;
  name: string;
  discountType: DiscountType;
  discountValue: number;
  minOrderAmount?: number;
  maxOrderAmount?: number;
  maxUsageCount?: number;
  usageCount: number;
  isActive: boolean;
  validFrom?: string;
  validUntil?: string;
  createdAt: string;
  updatedAt?: string;
}

// Request/Response DTOs
export interface RedeemPointsRequest {
  points: number;
}

export interface RedeemPointsResponse {
  discountAmount: number;
  pointsRedeemed: number;
  newBalance: number;
}

export interface PointsHistoryParams {
  page?: number;
  pageSize?: number;
}

export interface PointsCalculation {
  points: number;
  appliedRule?: PointEarningRule;
}

export interface DiscountCalculation {
  discountAmount: number;
  customerDiscount?: CustomerDiscountRule;
  pointsDiscount?: number;
  totalDiscount: number;
}

// Extended Order type with fidelity fields
export interface OrderWithFidelity {
  id: string;
  userId: string;
  deliveryAddressId?: string;
  subtotal: number;
  deliveryFee: number;
  totalAmount: number;
  status: string;
  paymentStatus: string;
  notes?: string;
  createdAt: string;
  updatedAt?: string;

  // Fidelity fields
  fidelityPointsEarned: number;
  fidelityPointsRedeemed: number;
  fidelityPointsDiscount: number;
  customerDiscountAmount: number;
  customerDiscountRuleId?: string;

  // Relations
  items: any[];
  deliveryAddress?: any;
}

// Order creation request with point redemption
export interface CreateOrderWithFidelityRequest {
  deliveryAddressId: string;
  notes?: string;
  redeemPoints?: number; // Optional: points to redeem for discount
}

// Order preview response with discount breakdown
export interface OrderPreview {
  subtotal: number;
  deliveryFee: number;
  customerDiscountAmount: number;
  fidelityPointsDiscount: number;
  totalDiscount: number;
  totalAmount: number;
  pointsToBeEarned: number;
  availablePointsDiscount: number; // Max discount from current points
  appliedDiscount?: CustomerDiscountRule;
}
