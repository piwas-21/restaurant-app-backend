import React, { useState, useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import { updateCategory, uploadCategoryImage, reorderCategory } from '@/services/categoryService';

const MAX_FILE_SIZE = 5 * 1024 * 1024; // 5MB
const ACCEPTED_IMAGE_TYPES = ['image/jpeg', 'image/jpg', 'image/png', 'image/webp'];

const editCategorySchema = z.object({
  name: z.string().min(1, { message: 'Category name is required' }),
  description: z.string().optional(),
  imageFile: z.any()
    .refine((files) => !files || files.length === 0 || files[0].size <= MAX_FILE_SIZE, `Max file size is 5MB.`)
    .refine(
      (files) => !files || files.length === 0 || ACCEPTED_IMAGE_TYPES.includes(files[0].type),
      '.jpg, .jpeg, .png and .webp files are accepted.'
    ).optional(),
  isActive: z.boolean(),
  displayOrder: z.coerce.number().int().min(0, { message: 'Display order must be a non-negative integer' }),
});

type EditCategoryFormValues = z.infer<typeof editCategorySchema>;

interface Category {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  displayOrder: number;
}

interface EditCategoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCategoryUpdated: () => void;
  category: Category | null;
}

const EditCategoryModal: React.FC<EditCategoryModalProps> = ({ isOpen, onClose, onCategoryUpdated, category }) => {
  const { t } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors },
    setError,
    reset,
  } = useForm<EditCategoryFormValues>({
    resolver: zodResolver(editCategorySchema),
  });

  useEffect(() => {
    if (category) {
      reset({
        name: category.name,
        description: category.description || '',
        isActive: category.isActive,
        displayOrder: category.displayOrder,
      });
    }
  }, [category, reset]);

  const onSubmit = async (data: EditCategoryFormValues) => {
    if (!category) return;

    setIsSubmitting(true);
    setError('root', { message: '' });

    try {
      // Step 1: Update main category details
      const updateData = {
        id: category.id,
        name: data.name,
        description: data.description,
        isActive: data.isActive,
      };
      const categoryResponse = await updateCategory(category.id, updateData);

      if (!categoryResponse.success) {
        // Handle main update failure
        if (categoryResponse.errors && Array.isArray(categoryResponse.errors) && categoryResponse.errors.length > 0) {
          const errorMessage = categoryResponse.errors[0];
          setError(errorMessage.toLowerCase().includes('name') ? 'name' : 'root', { type: 'manual', message: errorMessage });
        } else {
          setError('root', { message: categoryResponse.message || 'Failed to update category' });
        }
        setIsSubmitting(false);
        return;
      }

      // Step 2: Update display order if it has changed
      if (data.displayOrder !== category.displayOrder) {
        const reorderResponse = await reorderCategory(category.id, data.displayOrder);
        if (!reorderResponse.success) {
          // Handle reorder failure, but show partial success
          setError('root', { message: `Category details updated, but reorder failed: ${reorderResponse.message}` });
          // Continue to image upload if needed
        }
      }

      // Step 3: Upload image if a new one is provided
      const imageFile = data.imageFile?.[0];
      if (imageFile) {
        const imageUploadResponse = await uploadCategoryImage(category.id, imageFile);
        if (!imageUploadResponse.success) {
          // Handle image upload failure, but show partial success
          setError('root', { message: `Category updated, but image upload failed: ${imageUploadResponse.message}` });
        }
      }

      onCategoryUpdated();
      onClose();

    } catch (error) {
      setError('root', { message: 'An unexpected error occurred.' });
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className={styles.modalOverlay}>
      <div className={styles.modalContent}>
        <h2>{t('edit_category')}</h2>
        <form onSubmit={handleSubmit(onSubmit)}>
          {errors.root && <p className={styles.errorMessage}>{errors.root.message}</p>}
          <div className={styles.formGroup}>
            <label htmlFor="name">{t('category_name')}</label>
            <input id="name" {...register('name')} />
            {errors.name && <p className={styles.errorMessage}>{errors.name.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="description">{t('description')}</label>
            <textarea id="description" {...register('description')} />
            {errors.description && <p className={styles.errorMessage}>{errors.description.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="imageFile">{t('category_image_edit')}</label>
            <input id="imageFile" type="file" accept="image/*" {...register('imageFile')} />
            {errors.imageFile && <p className={styles.errorMessage}>{errors.imageFile.message as string}</p>}
          </div>
          <div className={`${styles.formGroup} ${styles.checkboxGroup}`}>
            <label htmlFor="isActive">{t('is_active')}</label>
            <input type="checkbox" id="isActive" {...register('isActive')} />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="displayOrder">{t('display_order')}</label>
            <input id="displayOrder" type="number" {...register('displayOrder')} />
            {errors.displayOrder && <p className={styles.errorMessage}>{errors.displayOrder.message}</p>}
          </div>
          <div className={styles.buttonGroup}>
            <button type="submit" className={styles.submitButton} disabled={isSubmitting}>
              {isSubmitting ? t('saving...') : t('save_changes')}
            </button>
            <button type="button" onClick={onClose} className={styles.cancelButton} disabled={isSubmitting}>
              {t('cancel')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default EditCategoryModal;
