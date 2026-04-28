import { z } from 'zod';

/**
 * E.164 validation matches the backend regex (`^\+[1-9]\d{6,14}$`).
 * 8-16 chars, leading `+`, country digit non-zero. Tighter than the spec
 * but blocks country-code-only entries. See backend
 * `PhoneNumberValidation` for rationale.
 */
const e164 = z.string().regex(/^\+[1-9]\d{6,14}$/, 'Phone must be in E.164 format (e.g. +41227863333)');

/** Form data for the singleton fields (no phone numbers — managed separately). */
export const restaurantInfoSchema = z.object({
  name: z.string().min(1, 'Name is required').max(200),
  addressLine1: z.string().min(1, 'Address line 1 is required').max(200),
  addressLine2: z.string().max(200).nullable().default(null),
  city: z.string().min(1, 'City is required').max(100),
  postalCode: z.string().min(1, 'Postal code is required').max(20),
  country: z.string().min(1, 'Country is required').max(100),
  // Latitude / longitude are optional; coerce empty string → null at the
  // form layer rather than fail validation.
  latitude: z.coerce.number().min(-90).max(90).nullable().default(null),
  longitude: z.coerce.number().min(-180).max(180).nullable().default(null),
  email: z.string().min(1, 'Email is required').email('Invalid email address').max(254),
  website: z
    .string()
    .max(2048)
    .url('Must be a valid URL (include https://)')
    .nullable()
    .or(z.literal('').transform(() => null))
    .default(null),
});

// Input = pre-default shape (what RHF stores). Output = post-default
// (what `data` in handleSubmit receives). Splitting these makes the
// useForm<Input> generic match the resolver without an `as any` cast.
export type RestaurantInfoFormInput = z.input<typeof restaurantInfoSchema>;
export type RestaurantInfoFormOutput = z.output<typeof restaurantInfoSchema>;
export type RestaurantInfoFormData = RestaurantInfoFormOutput;

/** Form data for adding / editing a phone number. */
export const phoneNumberSchema = z.object({
  label: z
    .string()
    .max(50)
    .nullable()
    .or(z.literal('').transform(() => null))
    .default(null),
  number: e164,
  whatsAppEnabled: z.boolean().default(false),
  displayOrder: z.coerce.number().int().min(0).default(0),
  isActive: z.boolean().default(true),
});

export type PhoneNumberFormInput = z.input<typeof phoneNumberSchema>;
export type PhoneNumberFormOutput = z.output<typeof phoneNumberSchema>;
export type PhoneNumberFormData = PhoneNumberFormOutput;
