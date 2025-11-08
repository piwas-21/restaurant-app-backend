import { useTranslation } from 'react-i18next';

/**
 * Custom hook providing utility functions for table-related translations and helpers
 */
export const useTableHelpers = () => {
  const { t } = useTranslation();

  /**
   * Get translated label for a table shape
   */
  const getShapeLabel = (shape: string | undefined): string => {
    const normalizedShape = (shape || 'circle').toLowerCase();
    return t(normalizedShape, normalizedShape === 'circle' ? 'Circle' : normalizedShape === 'square' ? 'Square' : 'Rectangle');
  };

  /**
   * Get CSS class name based on shape (for styling)
   */
  const getShapeClassName = (shape: string | undefined): string => {
    return (shape || 'circle').toLowerCase();
  };

  /**
   * All valid table shapes
   */
  const allShapes = ['circle', 'square', 'rectangle'];

  return {
    getShapeLabel,
    getShapeClassName,
    allShapes,
  };
};
