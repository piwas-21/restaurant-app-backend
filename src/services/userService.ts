import { apiClient } from './apiClient';

const USER_API_URL = `/api/User`;

export const fetchUsers = async (
  role: string,
  isDeleted: boolean,
  search: string,
  page: number,
  pageSize: number
) => {
  const params = new URLSearchParams({
    Role: role,
    IsDeleted: String(isDeleted),
    Search: search,
    Page: String(page),
    PageSize: String(pageSize),
  });

  const response = await apiClient.get(`${USER_API_URL}/users?${params.toString()}`);
  return response.json();
};

export const registerStaff = async (staffData: any) => {
  const response = await apiClient.post(`${USER_API_URL}/register/staff`, staffData);
  return response.json();
};

export const deleteStaff = async (userId: string) => {
  const response = await apiClient.delete(`${USER_API_URL}/delete/user`, { userId });
  return response.json();
};
