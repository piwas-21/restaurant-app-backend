import { useCallback, useState } from 'react';
import { useForm, type UseFormReturn } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { createProduct, type CreateProductData } from '@/services/menuService';
import { uploadBulkProductImages } from '@/services/productService';

const createProductSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  basePrice: z.number().min(0, 'Price must be 0 or greater'),
  type: z.string(),
  isActive: z.boolean(),
  isAvailable: z.boolean(),
  isSpecial: z.boolean(),
  categoryIds: z.array(z.string()),
  primaryCategoryId: z.string(),
  description: z.string().optional(),
  ingredients: z.array(z.string()),
  allergens: z.array(z.string()),
  variations: z.array(z.object({
    name: z.string(),
    description: z.string().optional(),
    priceModifier: z.number(),
    isActive: z.boolean(),
    displayOrder: z.number()
  })),
  content: z.array(z.object({
    name: z.string(),
    language: z.string(),
    description: z.string()
  })),
  preparationTimeMinutes: z.number().optional(),
  displayOrder: z.number().optional()
});

export type FormValues = z.infer<typeof createProductSchema>;

export type UseProductFormResult = {
  submissionStatus: 'idle' | 'creating' | 'uploading';
  imageFiles: File[];
  setImageFiles: (files: File[]) => void;
  register: UseFormReturn<FormValues>['register'];
  handleSubmit: UseFormReturn<FormValues>['handleSubmit'];
  formState: UseFormReturn<FormValues>['formState'];
  onSubmit: (data: FormValues) => Promise<void>;
  reset: () => void;
};

export const useProductForm = (onSuccess: () => void): UseProductFormResult => {
  const [submissionStatus, setSubmissionStatus] = useState<'idle' | 'creating' | 'uploading'>('idle');
  const [imageFiles, setImageFiles] = useState<File[]>([]);

  const {
    register,
    handleSubmit,
    formState,
    reset: resetForm,
    setError
  } = useForm<FormValues>({
    resolver: zodResolver(createProductSchema),
    defaultValues: {
      name: '',
      basePrice: 0,
      type: 'mainItem',
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      categoryIds: [],
      primaryCategoryId: '',
      content: [],
      variations: [],
      ingredients: [],
      allergens: []
    }
  } as const);

  const reset = useCallback(() => {
    resetForm();
    setImageFiles([]);
    setSubmissionStatus('idle');
  }, [resetForm]);

  const onSubmit = useCallback(async (data: FormValues) => {
    setSubmissionStatus('creating');
    try {
      // Format content for the API
      const formattedContent: { [key: string]: { name: string; description: string } } = {};
      data.content.forEach(item => {
        formattedContent[item.language] = {
          name: item.name,
          description: item.description
        };
      });

      const productData: CreateProductData = {
        name: data.name,
        basePrice: data.basePrice,
        type: data.type,
        isActive: data.isActive,
        isAvailable: data.isAvailable,
        isSpecial: data.isSpecial,
        categoryIds: data.categoryIds,
        primaryCategoryId: data.primaryCategoryId,
        description: data.description,
        ingredients: data.ingredients,
        allergens: data.allergens,
        variations: data.variations,
        content: formattedContent,
        preparationTimeMinutes: data.preparationTimeMinutes,
        displayOrder: data.displayOrder
      };

      const response = await createProduct(productData);

      if (!response.success) {
        if (response.errors && response.errors.length > 0) {
          response.errors.forEach((errorMsg: string) => {
            const field = errorMsg.toLowerCase().includes('name') ? 'name' :
              errorMsg.toLowerCase().includes('price') ? 'basePrice' : 'root';
            setError(field as keyof FormValues, {
              type: 'manual',
              message: errorMsg
            });
          });
        } else {
          setError('root', {
            type: 'manual',
            message: response.message || 'Failed to create product'
          });
        }
        setSubmissionStatus('idle');
        return;
      }

      if (imageFiles.length > 0) {
        setSubmissionStatus('uploading');
        await uploadBulkProductImages(response.data.id, imageFiles);
      }

      onSuccess();
      reset();

    } catch {
      setError('root', {
        type: 'manual',
        message: 'An unexpected error occurred'
      });
      setSubmissionStatus('idle');
    }
  }, [imageFiles, onSuccess, reset, setError]);

  return {
    submissionStatus,
    imageFiles,
    setImageFiles,
    register,
    handleSubmit,
    formState,
    onSubmit,
    reset
  };
};
