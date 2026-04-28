import React, { useEffect, useState } from 'react';
import Image from 'next/image';
import { getProductById } from '@/services/menuService';
import AllergenDisplay from '@/components/common/AllergenDisplay';
import styles from './MenuBundleDetails.module.css';

interface MenuSectionItemProduct {
  id: string;
  productId: string;
  productName?: string;
  additionalPrice: number;
  isDefault: boolean;
}

interface ProductCardProps {
  item: MenuSectionItemProduct;
}

interface ProductDetails {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  allergens?: string[];
  ingredients?: string[];
  images?: Array<{ url: string; altText?: string }>;
}

const ProductCard: React.FC<ProductCardProps> = ({ item }) => {
  const [product, setProduct] = useState<ProductDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    const fetchProductDetails = async () => {
      try {
        setLoading(true);
        setError(false);
        const response = (await getProductById(item.productId)) as { success: boolean; data?: ProductDetails };
        if (response.success && response.data) {
          setProduct(response.data);
        } else {
          setError(true);
        }
      } catch (err) {
        console.error('Error fetching product details:', err);
        setError(true);
      } finally {
        setLoading(false);
      }
    };

    fetchProductDetails();
  }, [item.productId]);

  if (loading) {
    return <div className={`${styles.productCard} ${styles.loadingSkeleton} ${styles.skeletonCard}`} />;
  }

  if (error || !product) {
    return (
      <div className={styles.productCard}>
        <div className={styles.errorMessage}>Failed to load product details</div>
      </div>
    );
  }

  const primaryImage = product.images?.find((img) => img.url) || product.images?.[0];
  const displayPrice = product.basePrice;
  const hasAdditionalPrice = item.additionalPrice > 0;

  return (
    <div className={`${styles.productCard} ${item.isDefault ? styles.default : ''}`}>
      {item.isDefault && <div className={styles.defaultBadge}>Default</div>}

      <div className={styles.productCardContent}>
        {primaryImage && (
          <Image
            src={primaryImage.url}
            alt={primaryImage.altText || product.name}
            className={styles.productImage}
            width={200}
            height={200}
          />
        )}

        <div className={styles.productInfo}>
          <h4 className={styles.productName}>{product.name}</h4>

          {product.description && <p className={styles.productDescription}>{product.description}</p>}

          <div className={styles.productPrice}>
            CHF {displayPrice.toFixed(2)}
            {hasAdditionalPrice && (
              <span className={styles.additionalPrice}>+CHF {item.additionalPrice.toFixed(2)}</span>
            )}
          </div>

          <div className={styles.productTags}>
            <AllergenDisplay allergens={product.allergens} variant="compact" maxVisible={3} showLabel={false} />

            {product.ingredients && product.ingredients.length > 0 && (
              <>
                {product.ingredients.slice(0, 2).map((ingredient, idx) => (
                  <span key={idx} className={styles.ingredientBadge}>
                    🥗 {ingredient}
                  </span>
                ))}
                {product.ingredients.length > 2 && (
                  <span className={styles.ingredientBadge}>+{product.ingredients.length - 2} more</span>
                )}
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductCard;
