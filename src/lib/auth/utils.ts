
import { z } from 'zod';

export const loginSchema = z.object({
  email: z.string().email(),
  password: z.string(),
});

export const staffRegistrationSchema = z.object({
  firstName: z.string(),
  lastName: z.string(),
  email: z.string().email(),
  password: z.string().min(8, "Password must be at least 8 characters long."),
  confirmPassword: z.string(),
  role: z.string(),
}).refine(data => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"],
});

export const customerRegistrationSchema = z.object({
  firstName: z.string(),
  lastName: z.string(),
  email: z.string().email(),
  password: z.string().min(8, "Password must be at least 8 characters long."),
  confirmPassword: z.string(),
}).refine(data => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"],
});


export async function login(formData: z.infer<typeof loginSchema>) {
  const response = await fetch(`${process.env.NEXT_PUBLIC_API_URL}/Auth/login`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });
  return response.json();
}

export async function registerStaff(formData: z.infer<typeof staffRegistrationSchema>, token: string) {
  const response = await fetch(`${process.env.NEXT_PUBLIC_API_URL}/Auth/register/staff`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
    },
    body: JSON.stringify(formData),
  });
  return response.json();
}

export async function registerCustomer(formData: z.infer<typeof customerRegistrationSchema>) {
  const response = await fetch(`${process.env.NEXT_PUBLIC_API_URL}/Auth/register/customer`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(formData),
  });
  return response.json();
}
