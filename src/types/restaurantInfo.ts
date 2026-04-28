/**
 * Frontend mirrors of the backend `Features/RestaurantInfo/Dtos/*` and
 * `Features/RestaurantInfo/Commands/*` shapes.
 *
 * Source of truth: `backend/RestaurantSystem.Api/Features/RestaurantInfo/`.
 * Keep field names and nullability in lock-step (camelCase serialiser on
 * the .NET side flips PascalCase property names to camelCase JSON, which
 * is what these types reflect).
 */

import type { ApiResponse } from '@/types/order';

export interface RestaurantPhoneNumberDto {
  id: string;
  label: string | null;
  /** E.164, e.g. `+41227863333`. */
  number: string;
  whatsAppEnabled: boolean;
  displayOrder: number;
  isActive: boolean;
}

export interface RestaurantInfoDto {
  id: string;
  name: string;
  addressLine1: string;
  addressLine2: string | null;
  city: string;
  postalCode: string;
  country: string;
  /** Decimal serialised as number; null when not set. */
  latitude: number | null;
  longitude: number | null;
  email: string;
  website: string | null;
  phoneNumbers: RestaurantPhoneNumberDto[];
}

// ── Commands ─────────────────────────────────────────────────────────

export interface UpdateRestaurantInfoCommand {
  name: string;
  addressLine1: string;
  addressLine2: string | null;
  city: string;
  postalCode: string;
  country: string;
  latitude: number | null;
  longitude: number | null;
  email: string;
  website: string | null;
}

export interface AddPhoneNumberCommand {
  label: string | null;
  number: string;
  whatsAppEnabled: boolean;
  displayOrder: number;
  isActive: boolean;
}

export interface UpdatePhoneNumberCommand {
  /** Echoed for symmetry with the backend record; the route id wins. */
  id: string;
  label: string | null;
  number: string;
  whatsAppEnabled: boolean;
  displayOrder: number;
  isActive: boolean;
}

// Re-export for convenience so callers can pull a single import.
export type { ApiResponse };
