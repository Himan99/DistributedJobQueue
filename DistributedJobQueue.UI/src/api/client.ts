import axios, { type AxiosInstance } from 'axios';

// Use relative URLs in dev (Vite proxy handles forwarding), absolute in prod
const isDev = import.meta.env.DEV;
const API_BASE_URL = isDev ? '' : (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5033');

export const createApiClient = (baseURL: string = API_BASE_URL): AxiosInstance => {
  const client = axios.create({
    baseURL,
    timeout: 30000,
    headers: {
      'Content-Type': 'application/json',
    },
  });

  client.interceptors.response.use(
    (response) => response,
    (error) => {
      const message = error.response?.data?.detail || error.message;
      return Promise.reject(new Error(message));
    }
  );

  return client;
};

export const apiClient = createApiClient();