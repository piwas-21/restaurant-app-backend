import { apiClient } from '@/utils/apiClient';
import {
  UserGroupDto,
  CreateUserGroupDto,
  UpdateUserGroupDto,
  GroupMembershipDto,
  AddMemberDto,
  GroupDiscountDto,
  CreateGroupDiscountDto,
  UpdateGroupDiscountDto,
  ValidateQRCodeDto,
  QRCodeValidationResult,
  ApiResponse
} from '@/types/userGroupTypes';

const USER_GROUPS_API_URL = '/api/UserGroup';
const GROUP_DISCOUNTS_API_URL = '/api/GroupDiscount';

// User Group Operations
export const getUserGroups = async () => {
  return await apiClient.get<ApiResponse<UserGroupDto[]>>(USER_GROUPS_API_URL);
};

export const getUserGroup = async (id: string) => {
  return await apiClient.get<ApiResponse<UserGroupDto>>(`${USER_GROUPS_API_URL}/${id}`);
};

export const createUserGroup = async (data: CreateUserGroupDto) => {
  return await apiClient.post<ApiResponse<UserGroupDto>>(USER_GROUPS_API_URL, data);
};

export const updateUserGroup = async (id: string, data: UpdateUserGroupDto) => {
  return await apiClient.put<ApiResponse<UserGroupDto>>(`${USER_GROUPS_API_URL}/${id}`, data);
};

export const deleteUserGroup = async (id: string) => {
  return await apiClient.delete<ApiResponse<boolean>>(`${USER_GROUPS_API_URL}/${id}`);
};

// Membership Operations
export const getGroupMembers = async (groupId: string) => {
  return await apiClient.get<ApiResponse<GroupMembershipDto[]>>(`${USER_GROUPS_API_URL}/${groupId}/members`);
};

export const addGroupMember = async (groupId: string, data: AddMemberDto) => {
  return await apiClient.post<ApiResponse<GroupMembershipDto>>(`${USER_GROUPS_API_URL}/${groupId}/members`, data);
};

export const removeGroupMember = async (groupId: string, userId: string) => {
  return await apiClient.delete<ApiResponse<boolean>>(`${USER_GROUPS_API_URL}/${groupId}/members/${userId}`);
};

export const getMemberQRCode = async (membershipId: string) => {
  // This endpoint returns an image file, so we handle it differently if needed
  // For now, we can construct the URL directly for <img> tags
  return `${process.env.NEXT_PUBLIC_API_URL || ''}${USER_GROUPS_API_URL}/membership/${membershipId}/qrcode`;
};

export const validateQRCode = async (qrCode: string) => {
  return await apiClient.post<ApiResponse<QRCodeValidationResult>>(`${USER_GROUPS_API_URL}/validate-qr`, { qrCode } as ValidateQRCodeDto);
};

export const calculateDiscount = async (membershipId: string, orderAmount: number) => {
  return await apiClient.get<ApiResponse<number>>(`${USER_GROUPS_API_URL}/membership/${membershipId}/discount?orderAmount=${orderAmount}`);
};

// Group Discount Operations
export const getGroupDiscounts = async (groupId: string) => {
  return await apiClient.get<ApiResponse<GroupDiscountDto[]>>(`${GROUP_DISCOUNTS_API_URL}/group/${groupId}`);
};

export const createGroupDiscount = async (groupId: string, data: CreateGroupDiscountDto) => {
  return await apiClient.post<ApiResponse<GroupDiscountDto>>(`${GROUP_DISCOUNTS_API_URL}?groupId=${groupId}`, data);
};

export const updateGroupDiscount = async (id: string, data: UpdateGroupDiscountDto) => {
  return await apiClient.put<ApiResponse<GroupDiscountDto>>(`${GROUP_DISCOUNTS_API_URL}/${id}`, data);
};

export const deleteGroupDiscount = async (id: string) => {
  return await apiClient.delete<ApiResponse<boolean>>(`${GROUP_DISCOUNTS_API_URL}/${id}`);
};
