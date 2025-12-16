/**
 * Auto Print Settings for Cashier Page
 */
export interface AutoPrintSettings {
  enabled: boolean;
  orderTypes: {
    dineIn: boolean;
    takeaway: boolean;
    delivery: boolean;
  };
  orderStatuses: {
    pending: boolean;
    confirmed: boolean;
    preparing: boolean;
    ready: boolean;
  };
  printContent: {
    all: boolean;
    frontKitchen: boolean;
    backKitchen: boolean;
    bill: boolean;
  };
}

/**
 * Default auto-print settings
 */
export const DEFAULT_AUTO_PRINT_SETTINGS: AutoPrintSettings = {
  enabled: false,
  orderTypes: {
    dineIn: true,
    takeaway: false,
    delivery: false,
  },
  orderStatuses: {
    pending: true,
    confirmed: false,
    preparing: false,
    ready: false,
  },
  printContent: {
    all: true,
    frontKitchen: false,
    backKitchen: false,
    bill: false,
  },
};
