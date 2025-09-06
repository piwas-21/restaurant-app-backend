import React, { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import { createCategory, uploadCategoryImage } from '@/services/categoryService';

const MAX_FILE_SIZE = 5 * 1024 * 1024; // 5MB
const ACCEPTED_IMAGE_TYPES = ['image/jpeg', 'image/jpg', 'image/png', 'image/webp'];

const createCategorySchema = z.object({
  name: z.string().min(1, { message: 'Category name is required' }),
  description: z.string().optional(),
  imageFile: z
    .any()
    .refine((files) => !files || files.length === 0 || files[0].size <= MAX_FILE_SIZE, `Max file size is 5MB.`)
    .refine(
      (files) => !files || files.length === 0 || ACCEPTED_IMAGE_TYPES.includes(files[0].type),
      '.jpg, .jpeg, .png and .webp files are accepted.'
    )
    .optional(),
  isActive: z.boolean(),
  displayOrder: z.coerce.number().int().min(0, { message: 'Display order must be a non-negative integer' }),
});

type CreateCategoryFormValues = z.infer<typeof createCategorySchema>;

interface CreateCategoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCategoryCreated: () => void;
}

const CreateCategoryModal: React.FC<CreateCategoryModalProps> = ({ isOpen, onClose, onCategoryCreated }) => {
  const { t } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors },
    setError,
    reset,
  } = useForm<CreateCategoryFormValues>({
    resolver: zodResolver(createCategorySchema),
    defaultValues: {
      isActive: true,
      displayOrder: 0,
    },
  });

  const onSubmit = async (data: CreateCategoryFormValues) => {
    setIsSubmitting(true);
    setError('root', { message: '' }); // Clear previous errors

    try {
      // Step 1: Create the category without the image
      const categoryResponse = await createCategory({
        name: data.name,
        description: data.description,
        isActive: data.isActive,
        displayOrder: data.displayOrder,
      });

      if (!categoryResponse.success) {
        // Handle category creation errors
        if (categoryResponse.errors && Array.isArray(categoryResponse.errors) && categoryResponse.errors.length > 0) {
          const errorMessage = categoryResponse.errors[0];
          if (errorMessage.toLowerCase().includes('name')) {
            setError('name', { type: 'manual', message: errorMessage });
          } else {
            setError('root', { message: errorMessage });
          }
        } else {
          setError('root', { message: categoryResponse.message || 'Failed to create category' });
        }
        setIsSubmitting(false);
        return;
      }

      // Step 2: If an image is provided, upload it
      const imageFile = data.imageFile?.[0];
      if (imageFile) {
        const newCategoryId = categoryResponse.data.id;
        const imageUploadResponse = await uploadCategoryImage(newCategoryId, imageFile);

        if (!imageUploadResponse.success) {
          // Handle image upload error, but the category is already created.
          // We can show a partial success message.
          setError('root', { message: `Category created, but image upload failed: ${imageUploadResponse.message}` });
          setIsSubmitting(false);
          // Still call these to update the list and close the modal
          onCategoryCreated();
          onClose();
          reset();
          return;
        }
      }

      // If all steps are successful
      onCategoryCreated();
      onClose();
      reset();

    } catch (error) {
      setError('root', { message: 'An unexpected error occurred.' });
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay}>
      <div className={styles.modalContent}>
        <h2>{t('create_category')}</h2>
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
            <label htmlFor="imageFile">{t('category_image')}</label>
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
              {isSubmitting ? t('creating...') : t('create')}
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

export default CreateCategoryModal;
