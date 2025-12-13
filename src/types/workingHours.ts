export interface WorkingHoursDto {
  id: string;
  dayOfWeek: string | number; // API returns string ("Sunday"), we need number (0-6)
  openTime: string; // "HH:mm:ss" format
  closeTime: string; // "HH:mm:ss" format
  isActive: boolean;
  isClosed: boolean;
  notes?: string | null;
}

export interface UpdateWorkingHoursDto {
  dayOfWeek: number;
  openTime: string;
  closeTime: string;
  isActive: boolean;
  isClosed: boolean;
  notes?: string | null;
}

// Helper to convert day name to number
export const dayNameToNumber = (day: string | number): number => {
  if (typeof day === 'number') return day;
  
  const dayMap: Record<string, number> = {
    'Sunday': 0,
    'Monday': 1,
    'Tuesday': 2,
    'Wednesday': 3,
    'Thursday': 4,
    'Friday': 5,
    'Saturday': 6
  };
  
  return dayMap[day] ?? 0;
};
