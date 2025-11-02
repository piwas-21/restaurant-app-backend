import { z } from 'zod';
import { UserRole } from '@/types/user';

export const loginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(6),
});

export const staffRegistrationSchema = z.object({
  email: z.string().email(),
  password: z.string().min(6),
  confirmPassword: z.string().min(6),
  firstName: z.string().min(2),
  lastName: z.string().min(2),
  role: z.nativeEnum(UserRole),
}).refine((data) => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"],
});

export const customerRegistrationSchema = z.object({
  email: z.string().email(),
  password: z.string().min(6),
  confirmPassword: z.string().min(6),
  firstName: z.string().min(2),
  lastName: z.string().min(2),
}).refine((data) => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"], // path of error
});
