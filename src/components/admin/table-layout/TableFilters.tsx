import React from 'react';
import styles from './TableFilters.module.css';

interface TableFiltersProps {
  filters: {
    showIndoor: boolean;
    showOutdoor: boolean;
    showActive: boolean;
    showInactive: boolean;
  };
  onFilterChange: (filters: TableFiltersProps['filters']) => void;
}

export default function TableFilters({ filters, onFilterChange }: TableFiltersProps) {
  const handleToggle = (key: keyof typeof filters) => {
    onFilterChange({ ...filters, [key]: !filters[key] });
  };

  return (
    <div className={styles.filters}>
      <h3 className={styles.filtersTitle}>Filters</h3>

      <div className={styles.filterSection}>
        <label className={styles.sectionLabel}>Location</label>
        <div className={styles.chipContainer}>
          <button
            type="button"
            onClick={() => handleToggle('showIndoor')}
            className={`${styles.chip} ${filters.showIndoor ? styles.chipActive : styles.chipInactive}`}
          >
            🏠 Indoor
          </button>
          <button
            type="button"
            onClick={() => handleToggle('showOutdoor')}
            className={`${styles.chip} ${filters.showOutdoor ? styles.chipActive : styles.chipInactive}`}
          >
            🌳 Outdoor
          </button>
        </div>
      </div>

      <div className={styles.filterSection}>
        <label className={styles.sectionLabel}>Status</label>
        <div className={styles.chipContainer}>
          <button
            type="button"
            onClick={() => handleToggle('showActive')}
            className={`${styles.chip} ${filters.showActive ? styles.chipActive : styles.chipInactive}`}
          >
            ✓ Active
          </button>
          <button
            type="button"
            onClick={() => handleToggle('showInactive')}
            className={`${styles.chip} ${filters.showInactive ? styles.chipActive : styles.chipInactive}`}
          >
            ○ Inactive
          </button>
        </div>
      </div>
    </div>
  );
}
