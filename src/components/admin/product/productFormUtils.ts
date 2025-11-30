import { UseFormSetError, UseFormReset } from 'react-hook-form';
import { FormData, EditFormData } from './schemas';
import { createProduct, createMenuBundle, updateMenuBundle } from '@/services/menuService';
import { updateProduct, uploadBulkProductImages } from '@/services/productService';

interface SubmitProductFormParams {
  data: FormData;
  imageFiles: File[];
  currentLanguage: string;
  detailedIngredients?: any[];
  setSubmissionStatus: (status: 'idle' | 'creating' | 'uploading') => void;
  setError: UseFormSetError<FormData>;
  onProductCreated: () => void;
  onClose: () => void;
  reset: UseFormReset<FormData>;
  setImageFiles: (files: File[]) => void;
}

interface SubmitEditProductFormParams {
  data: EditFormData;
  product: any;
  imageFiles: File[];
  detailedIngredients?: any[];
  setIsSubmitting: (status: boolean) => void;
  setError: UseFormSetError<EditFormData>;
  onProductUpdated: () => void;
  onClose: () => void;
}

export const submitProductForm = async ({
  data,
  imageFiles,
  currentLanguage,
  detailedIngredients,
  setSubmissionStatus,
  setError,
  onProductCreated,
  onClose,
  reset,
  setImageFiles,
}: SubmitProductFormParams) => {
  setSubmissionStatus('creating');
  try {
    // Format content for the API
    const content: { [key: string]: { name: string; description: string } } = {};

    // Automatically add the main product data to content using the current user language
    content[currentLanguage] = {
      name: data.name,
      description: data.description || ''
    };

    // Add any additional multilingual content
    data.content?.forEach(item => {
      if (item.language && item.language !== currentLanguage) {
        content[item.language] = {
          name: item.name,
          description: item.description || ''
        };
      }
    });

    // Clean detailedIngredients - remove temporary IDs for new ingredients
    const cleanedIngredients = (detailedIngredients || []).map((ing: any) => {
      const cleaned = { ...ing };
      // If ID starts with "temp-", it's a new ingredient - remove the ID
      if (typeof cleaned.id === 'string' && cleaned.id.startsWith('temp-')) {
        delete cleaned.id;
      }
      return cleaned;
    });



// ...

    // Format the product data
    const productData = {
      ...data,
      content,
      primaryCategoryId: data.primaryCategoryId || null,
      variations: data.variations || [],
      detailedIngredients: cleanedIngredients,
      menuDefinition: data.menuDefinition ? {
        ...data.menuDefinition,
        id: data.menuDefinition.id || null,
        sections: data.menuDefinition.sections?.map((section: any) => ({
          ...section,
          id: (section.id && (section.id.startsWith('temp-') || section.id === '')) ? null : section.id,
        })) || [],
        startTime: data.menuDefinition.startTime ? (data.menuDefinition.startTime.length === 5 ? `${data.menuDefinition.startTime}:00` : data.menuDefinition.startTime) : null,
        endTime: data.menuDefinition.endTime ? (data.menuDefinition.endTime.length === 5 ? `${data.menuDefinition.endTime}:00` : data.menuDefinition.endTime) : null,
      } : undefined
    };

    let productResponse;
    if (data.menuDefinition) {
       // It's a menu bundle
       productResponse = await createMenuBundle(productData) as { success: boolean; data?: { id: string }; message?: string };
    } else {
       productResponse = await createProduct(productData) as { success: boolean; data?: { id: string }; message?: string };
    }
    if (productResponse.success && productResponse.data?.id) {
      if (imageFiles.length > 0) {
        setSubmissionStatus('uploading');
        const imageResponse = await uploadBulkProductImages(productResponse.data.id, imageFiles) as { success: boolean; message?: string };
        if (!imageResponse.success) {
          // eslint-disable-next-line no-console
          console.error("Image upload failed:", imageResponse.message);
        }
      }
      onProductCreated();
      onClose();
      reset();
      setImageFiles([]);
    } else {
      setError('root', { message: productResponse.message || 'Failed to create product' });
    }
  } catch (error: any) {
    console.error("Submit error:", error);
    // Extract meaningful error message from backend response
    let errorMessage = 'An unexpected error occurred.';
    if (error?.response?.data) {
        if (error.response.data.errors) {
            // Combine validation errors
            errorMessage = Object.values(error.response.data.errors).flat().join(', ');
        } else if (error.response.data.title) {
            errorMessage = error.response.data.title;
        } else if (error.response.data.message) {
            errorMessage = error.response.data.message;
        }
    } else if (error?.message) {
        errorMessage = error.message;
    }
    setError('root', { message: errorMessage });
  } finally {
    setSubmissionStatus('idle');
  }
};

export const submitEditProductForm = async ({
  data,
  product,
  imageFiles,
  detailedIngredients,
  setIsSubmitting,
  setError,
  onProductUpdated,
  onClose,
}: SubmitEditProductFormParams) => {
  setIsSubmitting(true);
  try {
    const parseNum = (val: any, fallback: number): number => {
      const num = parseFloat(String(val || '').trim());
      return isNaN(num) ? fallback : num;
    };

    // Clean content array and format for API
    const cleanedContentArray = (data.content || [])
      .filter((e: any) => e?.language?.trim() && e?.name?.trim())
      .map((e: any) => ({
        language: String(e.language).trim(),
        name: String(e.name || '').trim(),
        description: (e.description ?? '').toString(),
      }));

    const formattedContent = cleanedContentArray.length > 0
      ? cleanedContentArray.reduce((acc: any, curr: any) => {
          acc[curr.language] = {
            name: curr.name,
            description: curr.description
          };
          return acc;
        }, {})
      : undefined;

    const categoryIds = Array.isArray(data.categoryIds) ? data.categoryIds.filter(Boolean) as string[] : [];
    let primaryCategoryId = (data.primaryCategoryId || '') as string;
    if (categoryIds.length > 0 && !categoryIds.includes(primaryCategoryId)) {
      primaryCategoryId = categoryIds[0];
    }

    const cleanedVariations = (data.variations || [])
      .filter(v => (v?.name || '').trim().length > 0)
      .map(v => ({
        id: v.id,
        name: (v.name || '').trim(),
        description: v.description ?? '',
        priceModifier: parseNum(v.priceModifier, 0),
        isActive: v.isActive ?? true,
        displayOrder: Number.isInteger(v.displayOrder as any) ? (v.displayOrder as any) : 0,
      }));

    // Clean detailedIngredients - remove temporary IDs for new ingredients
    const cleanedIngredients = (detailedIngredients || []).map((ing: any) => {
      const cleaned = { ...ing };
      // If ID starts with "temp-", it's a new ingredient - remove the ID
      if (typeof cleaned.id === 'string' && cleaned.id.startsWith('temp-')) {
        delete cleaned.id;
      }
      return cleaned;
    });

    const productData = {
      ...data,
      id: product.id,
      name: (data.name || '').trim(),
      description: (data.description ?? '').toString(),
      basePrice: parseNum(data.basePrice, 0),
      preparationTimeMinutes: typeof data.preparationTimeMinutes === 'number' ? data.preparationTimeMinutes : parseInt(String(data.preparationTimeMinutes || '0'), 10) || 0,
      allergens: Array.isArray(data.allergens) ? data.allergens.filter(Boolean) : [],

      categoryIds: categoryIds || [],
      primaryCategoryId: primaryCategoryId || null,
      variations: cleanedVariations,
      content: formattedContent,
      detailedIngredients: cleanedIngredients,
      menuDefinition: data.menuDefinition ? {
        ...data.menuDefinition,
        id: data.menuDefinition.id || null,
        sections: data.menuDefinition.sections?.map((section: any) => ({
          ...section,
          id: (section.id && (section.id.startsWith('temp-') || section.id === '')) ? null : section.id,
        })) || [],
        startTime: data.menuDefinition.startTime ? (data.menuDefinition.startTime.length === 5 ? `${data.menuDefinition.startTime}:00` : data.menuDefinition.startTime) : null,
        endTime: data.menuDefinition.endTime ? (data.menuDefinition.endTime.length === 5 ? `${data.menuDefinition.endTime}:00` : data.menuDefinition.endTime) : null,
      } : undefined
    } as any;

    const response = await updateProduct(product.id, productData) as { success: boolean; message?: string };
    if (response.success) {
      if (imageFiles.length > 0) {
        await uploadBulkProductImages(product.id, imageFiles);
      }
      onProductUpdated();
      onClose();
    } else {
      setError('root', { message: response.message || 'Failed to update product' });
    }
  } catch {
    setError('root', { message: 'An unexpected error occurred.' });
  } finally {
    setIsSubmitting(false);
  }
};
