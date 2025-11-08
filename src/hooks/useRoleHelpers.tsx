import { useTranslation } from 'react-i18next';

/**
 * Custom hook providing utility functions for user role management
 */
export const useRoleHelpers = () => {
  const { t } = useTranslation();

  /**
   * Convert role name from API (PascalCase) to translation key (kebab-case)
   * Examples:
   * - "Admin" → "admin"
   * - "Customer" → "customer"
   * - "KitchenStaff" → "kitchen-staff"
   * - "Cashier" → "cashier"
   * - "Server" → "server"
   */
  const roleToTranslationKey = (role: string): string => {
    // Insert hyphens before uppercase letters and convert to lowercase
    return role
      .replace(/([A-Z])/g, '-$1')
      .toLowerCase()
      .replace(/^-/, ''); // Remove leading hyphen if any
  };

  /**
   * Get translated label for a user role
   */
  const getRoleLabel = (role: string): string => {
    const key = roleToTranslationKey(role);
    return t(`roles.${key}`, role);
  };

  /**
   * Get CSS class name based on role (for styling badges)
   */
  const getRoleClassName = (role: string): string => {
    return role.toLowerCase();
  };

  /**
   * All valid user roles
   */
  const allRoles = ['Admin', 'Customer', 'Cashier', 'KitchenStaff', 'Server'];

  /**
   * All staff roles (non-customer roles)
   */
  const staffRoles = ['Server', 'Cashier', 'KitchenStaff', 'Admin'];

  return {
    getRoleLabel,
    roleToTranslationKey,
    getRoleClassName,
    allRoles,
    staffRoles,
  };
};
