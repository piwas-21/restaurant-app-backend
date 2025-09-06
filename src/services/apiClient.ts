import { refreshToken } from './authService';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL;

async function request(url: string, options: RequestInit = {}) {
  const token = localStorage.getItem('token');
  const headers = new Headers(options.headers || {});

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  options.headers = headers;

  const fullUrl = `${API_BASE_URL}${url}`;

  let response = await fetch(fullUrl, options);

  if (response.status === 401) {
    const newTokenData = await refreshToken();
    if (newTokenData.success) {
      headers.set('Authorization', `Bearer ${newTokenData.data.accessToken}`);
      options.headers = headers;
      response = await fetch(fullUrl, options);
    }
  }

  return response;
}

export const apiClient = {
  get: (url: string, options?: RequestInit) => request(url, { ...options, method: 'GET' }),
  post: (url: string, body: any, options?: RequestInit) => {
    const postOptions = {
      ...options,
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...options?.headers,
      },
      body: JSON.stringify(body),
    };
    return request(url, postOptions);
  },
  postFormData: (url: string, formData: FormData, options?: RequestInit) => {
    const postOptions = {
      ...options,
      method: 'POST',
      body: formData,
    };
    return request(url, postOptions);
  },
  put: (url: string, body: any, options?: RequestInit) => {
    const putOptions = {
      ...options,
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        ...options?.headers,
      },
      body: JSON.stringify(body),
    };
    return request(url, putOptions);
  },
  putFormData: (url: string, formData: FormData, options?: RequestInit) => {
    const putOptions = {
      ...options,
      method: 'PUT',
      body: formData,
    };
    return request(url, putOptions);
  },
  delete: (url: string, body?: any, options?: RequestInit) => {
    const deleteOptions: RequestInit = {
      ...options,
      method: 'DELETE',
    };
    if (body) {
      deleteOptions.headers = {
        'Content-Type': 'application/json',
        ...options?.headers,
      };
      deleteOptions.body = JSON.stringify(body);
    }
    return request(url, deleteOptions);
  }
};
