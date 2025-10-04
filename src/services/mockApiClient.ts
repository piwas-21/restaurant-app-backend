// Mock API client for testing when backend is not available
const MOCK_PRODUCTS_KEY = 'mock_products';
const MOCK_CATEGORIES_KEY = 'mock_categories';

// Initialize with some sample data
const initializeMockData = () => {
  if (!localStorage.getItem(MOCK_PRODUCTS_KEY)) {
    const sampleProducts = [
      {
        id: '4d4045cd-4657-4778-a9b9-e85bcf92f4fe',
        name: 'adana kebap',
        description: 'Spicy Turkish kebab',
        basePrice: 7.90,
        isActive: true,
        isAvailable: true,
        isSpecial: false,
        type: 'mainItem',
        categories: [{ categoryId: '1' }],
        primaryCategoryId: '1'
      },
      {
        id: '39f9fa0e-8491-456b-9d6f-b29f78fc668c',
        name: 'chicken durum 6',
        description: 'Chicken wrap',
        basePrice: 9.60,
        isActive: true,
        isAvailable: true,
        isSpecial: false,
        type: 'mainItem',
        categories: [{ categoryId: '6' }],
        primaryCategoryId: '6'
      }
    ];
    localStorage.setItem(MOCK_PRODUCTS_KEY, JSON.stringify(sampleProducts));
  }

  if (!localStorage.getItem(MOCK_CATEGORIES_KEY)) {
    const sampleCategories = [
      { id: '1', name: 'Starters' },
      { id: '2', name: 'Grills' },
      { id: '3', name: 'Soups' },
      { id: '4', name: 'Drinks' },
      { id: '5', name: 'Dessert' },
      { id: '6', name: 'Dürüm Wraps' }
    ];
    localStorage.setItem(MOCK_CATEGORIES_KEY, JSON.stringify(sampleCategories));
  }
};

const generateId = () => {
  return 'mock-' + Math.random().toString(36).substr(2, 9);
};

export const mockApiClient = {
  // Products
  getProducts: async (pageNumber = 1, pageSize = 10, categoryId?: string | null) => {
    initializeMockData();
    const products = JSON.parse(localStorage.getItem(MOCK_PRODUCTS_KEY) || '[]');
    let filteredProducts = products;

    if (categoryId) {
      filteredProducts = products.filter((p: any) =>
        p.categories?.some((c: any) => c.categoryId === categoryId) ||
        p.primaryCategoryId === categoryId
      );
    }

    return {
      success: true,
      message: 'Products retrieved successfully',
      data: {
        items: filteredProducts,
        totalCount: filteredProducts.length,
        page: pageNumber,
        pageSize: pageSize,
        totalPages: Math.ceil(filteredProducts.length / pageSize)
      },
      errors: null
    };
  },

  createProduct: async (productData: any) => {
    initializeMockData();
    const products = JSON.parse(localStorage.getItem(MOCK_PRODUCTS_KEY) || '[]');

    const newProduct = {
      ...productData,
      id: generateId(),
      categories: productData.categoryIds?.map((id: string) => ({ categoryId: id })) || []
    };

    products.push(newProduct);
    localStorage.setItem(MOCK_PRODUCTS_KEY, JSON.stringify(products));

    return {
      success: true,
      message: 'Product created successfully',
      data: newProduct,
      errors: null
    };
  },

  getProductById: async (productId: string) => {
    initializeMockData();
    const products = JSON.parse(localStorage.getItem(MOCK_PRODUCTS_KEY) || '[]');
    const product = products.find((p: any) => p.id === productId);

    if (!product) {
      return {
        success: false,
        message: 'Product not found',
        data: null,
        errors: ['Product not found']
      };
    }

    return {
      success: true,
      message: 'Product retrieved successfully',
      data: product,
      errors: null
    };
  },

  updateProduct: async (productId: string, productData: any) => {
    initializeMockData();
    const products = JSON.parse(localStorage.getItem(MOCK_PRODUCTS_KEY) || '[]');
    const productIndex = products.findIndex((p: any) => p.id === productId);

    if (productIndex === -1) {
      return {
        success: false,
        message: 'Product not found',
        data: null,
        errors: ['Product not found']
      };
    }

    const updatedProduct = {
      ...products[productIndex],
      ...productData,
      categories: productData.categoryIds?.map((id: string) => ({ categoryId: id })) || products[productIndex].categories
    };

    products[productIndex] = updatedProduct;
    localStorage.setItem(MOCK_PRODUCTS_KEY, JSON.stringify(products));

    return {
      success: true,
      message: 'Product updated successfully',
      data: updatedProduct,
      errors: null
    };
  },

  deleteProduct: async (productId: string) => {
    initializeMockData();
    const products = JSON.parse(localStorage.getItem(MOCK_PRODUCTS_KEY) || '[]');
    const filteredProducts = products.filter((p: any) => p.id !== productId);

    localStorage.setItem(MOCK_PRODUCTS_KEY, JSON.stringify(filteredProducts));

    return {
      success: true,
      message: 'Product deleted successfully',
      data: null,
      errors: null
    };
  },

  // Categories
  getCategories: async () => {
    initializeMockData();
    const categories = JSON.parse(localStorage.getItem(MOCK_CATEGORIES_KEY) || '[]');

    return {
      success: true,
      message: 'Categories retrieved successfully',
      data: {
        items: categories,
        totalCount: categories.length,
        page: 1,
        pageSize: categories.length,
        totalPages: 1
      },
      errors: null
    };
  }
};
