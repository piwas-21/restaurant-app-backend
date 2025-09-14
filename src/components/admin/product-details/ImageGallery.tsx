'use client';

import React, { useState, useEffect } from 'react';
import { ProductImage } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';

interface ImageGalleryProps {
  images: ProductImage[];
  productName: string;
}

const ImageGallery: React.FC<ImageGalleryProps> = ({ images, productName }) => {
  const [selectedImage, setSelectedImage] = useState<ProductImage | null>(null);

  useEffect(() => {
    if (images && images.length > 0) {
      const primary = images.find(img => img.isPrimary) || images[0];
      setSelectedImage(primary);
    }
  }, [images]);

  if (!images || images.length === 0) {
    return null;
  }

  return (
    <div className={detailsStyles.imageGalleryContainer}>
      <div className={detailsStyles.primaryImageContainer}>
        <img 
          src={selectedImage?.url} 
          alt={selectedImage?.altText || productName} 
          className={detailsStyles.primaryImage} 
        />
      </div>
      <div className={detailsStyles.thumbnailContainer}>
        {images.map((img, index) => (
          <img
            key={index}
            src={img.url}
            alt={img.altText}
            className={`${detailsStyles.thumbnail} ${selectedImage?.url === img.url ? detailsStyles.active : ''}`}
            onClick={() => setSelectedImage(img)}
          />
        ))}
      </div>
    </div>
  );
};

export default ImageGallery;
