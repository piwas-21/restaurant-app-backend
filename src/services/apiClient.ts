import { refreshToken } from './authService';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL;

const logout = () => {
  localStorage.removeItem('token');
  localStorage.removeItem('refreshToken');
  window.location.href = '/auth/login';
};

const isApiUnavailable = (error: any) => {
  // Check if it's a network error or connection refused
  return error instanceof TypeError ||
         error.message?.includes('fetch') ||
         error.message?.includes('Failed to fetch') ||
         error.code === 'NETWORK_ERROR' ||
         error.name === 'NetworkError';
};

const fetchWithAuth = async (url: string, options: RequestInit = {}) => {
  let token = localStorage.getItem('token');

  const headers = new Headers(options.headers || {});
  if (token) {
    headers.append('Authorization', `Bearer ${token}`);
  }

  if (!(options.body instanceof FormData)) {
    headers.append('Content-Type', 'application/json');
  }

  options.headers = headers;

  try {
    let response = await fetch(url, options);

    if (response.status === 401) {
      try {
        const refreshResponse = await refreshToken();
        if (refreshResponse.success) {
          token = localStorage.getItem('token');
          if (token) {
            headers.set('Authorization', `Bearer ${token}`);
            options.headers = headers;
            response = await fetch(url, options); // Retry the request
          }
        } else {
          logout(); // Logout if refresh fails
          return Promise.reject(refreshResponse);
        }
      } catch (refreshError) {
        logout();
        return Promise.reject(refreshError);
      }
    }

    return response;
  } catch (error) {
    // If it's a network error, throw it so the calling service can handle it
    if (isApiUnavailable(error)) {
      throw new Error('API_UNAVAILABLE');
    }
    throw error;
  }
};

const apiClient = {
  get: (endpoint: string) => fetchWithAuth(`${API_BASE_URL}${endpoint}`),
  post: (endpoint: string, body: any) => fetchWithAuth(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    body: JSON.stringify(body),
  }),
  put: (endpoint: string, body: any) => fetchWithAuth(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  }),
  delete: (endpoint: string) => fetchWithAuth(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
  }),
  postFormData: (endpoint: string, formData: FormData) => fetchWithAuth(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    body: formData,
  }),
  putFormData: (endpoint: string, formData: FormData) => fetchWithAuth(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    body: formData,
  }),
};

export { apiClient };
