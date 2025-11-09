# Pagination Fix - Parameter Case Mismatch

## Problem
Pagination in the menu page was not working - all pages showed the same items. The issue affected multiple endpoints across the application.

## Root Cause
**Frontend-Backend Parameter Case Mismatch**

The frontend was sending query parameters in camelCase:
- `pageNumber=1`
- `pageSize=10`

But the backend C# API expects PascalCase:
- `Page=1` or `PageNumber=1`
- `PageSize=10`

This caused the backend to ignore the page parameter and always return page 1.

## Files Fixed

### 1. Menu Service (`src/services/menuService.ts`)
**Before:**
```typescript
let url = `${PRODUCTS_API_URL}?pageNumber=${pageNumber}&pageSize=${pageSize}`;
```

**After:**
```typescript
// Backend expects 'Page' and 'PageSize' (PascalCase)
let url = `${PRODUCTS_API_URL}?Page=${pageNumber}&PageSize=${pageSize}`;
```

### 2. Category Service (`src/services/categoryService.ts`)
**Before:**
```typescript
const url = `${CATEGORIES_API_URL}?pageNumber=${pageNumber}&pageSize=${pageSize}`;
```

**After:**
```typescript
// Backend expects 'PageNumber' and 'PageSize' (PascalCase)
const url = `${CATEGORIES_API_URL}?PageNumber=${pageNumber}&PageSize=${pageSize}`;
```

### 3. Product Service (`src/services/productService.ts`)
**Before:**
```typescript
return await apiClient.get(`${PRODUCTS_API_URL}/specials?page=${page}&pageSize=${pageSize}`);
```

**After:**
```typescript
// Backend expects 'Page' and 'PageSize' (PascalCase)
return await apiClient.get(`${PRODUCTS_API_URL}/specials?Page=${page}&PageSize=${pageSize}`);
```

## Backend Parameter Names by Endpoint

| Endpoint | Query Parameters |
|----------|-----------------|
| `/api/Products` | `Page`, `PageSize`, `CategoryId` |
| `/api/Products/specials` | `Page`, `PageSize` |
| `/api/Categories` | `PageNumber`, `PageSize` |

## Testing
1. Navigate to the menu page
2. Click through pages 1, 2, 3
3. Verify different items are shown on each page
4. Test with different categories selected
5. Verify pagination info shows correct ranges

## Prevention
Consider implementing:
1. Shared TypeScript types for API query parameters
2. Automated tests for pagination
3. API parameter validation in the backend with clear error messages
4. Linting rules to catch parameter case mismatches
