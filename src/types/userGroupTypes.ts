export enum DiscountType {
  Percentage = 'Percentage',
  FixedAmount = 'FixedAmount'
}

export interface GroupDiscountDto {
  id: string;
  groupId: string;
  name: string;
  type: DiscountType;
  value: number;
  minimumOrderAmount?: number;
  maximumDiscountAmount?: number;
  isActive: boolean;
}

export interface CreateGroupDiscountDto {
  name: string;
  type: DiscountType;
  value: number;
  minimumOrderAmount?: number;
  maximumDiscountAmount?: number;
}

export interface UpdateGroupDiscountDto {
  id: string;
  name: string;
  type: DiscountType;
  value: number;
  minimumOrderAmount?: number;
  maximumDiscountAmount?: number;
  isActive: boolean;
}

export interface GroupMembershipDto {
  id: string;
  groupId: string;
  userId: string;
  userEmail: string;
  userName: string;
  uniqueQRCode: string;
  isActive: boolean;
  joinedAt: string;
  expiresAt?: string;
}

export interface AddMemberDto {
  userId: string;
  expiresAt?: string;
}

export interface UserGroupDto {
  id: string;
  name: string;
  description: string;
  qrCodeData: string;
  isActive: boolean;
  validFrom?: string;
  validUntil?: string;
  memberCount: number;
  discounts: GroupDiscountDto[];
}

export interface CreateUserGroupDto {
  name: string;
  description: string;
  validFrom?: string;
  validUntil?: string;
  initialDiscount?: CreateGroupDiscountDto;
}

export interface UpdateUserGroupDto {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  validFrom?: string;
  validUntil?: string;
}

export interface ValidateQRCodeDto {
  qrCode: string;
}

export interface QRCodeValidationResult {
  isValid: boolean;
  message: string;
  membership?: GroupMembershipDto;
  group?: UserGroupDto;
  applicableDiscounts: GroupDiscountDto[];
}

export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
  errors?: string[];
}
