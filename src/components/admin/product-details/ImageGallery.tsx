'use client';

import React, { useState, useEffect, useRef } from 'react';
import { ProductImage } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import ImageActions from './ImageActions';
import { updateProductImageDetails, deleteProductImage, uploadBulkProductImages } from '@/services/productService';
import { useParams } from 'next/navigation';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';

interface ImageGalleryProps {
  images: ProductImage[];
  productName: string;
  onImageUpdate: () => void;
}

const ImageGallery: React.FC<ImageGalleryProps> = ({ images = [], productName, onImageUpdate }) => {
  const [selectedImage, setSelectedImage] = useState<ProductImage | null>(null);
  const [imageList, setImageList] = useState<ProductImage[]>(images || []);
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [stagedFiles, setStagedFiles] = useState<File[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const params = useParams();
  const productId = params.productId as string;
  const { t } = useTranslation();

  useEffect(() => {
    const validImages = images || [];
    setImageList(validImages);
    if (validImages.length > 0) {
      const primary = validImages.find(img => img.isPrimary) || validImages[0];
      setSelectedImage(primary);
    } else {
      setSelectedImage(null);
    }
  }, [images]);

  const handleSaveChanges = async () => {
    if (selectedImage) {
      await updateProductImageDetails(productId, selectedImage.id, selectedImage);
      onImageUpdate();
    }
  };

  const handleSetPrimary = () => {
    if (selectedImage) {
      const updatedImages = imageList.map(img => ({
        ...img,
        isPrimary: img.id === selectedImage.id,
      }));
      setImageList(updatedImages);
      setSelectedImage({ ...selectedImage, isPrimary: true });
    }
  };

  const handleSortOrderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newSortOrder = parseInt(e.target.value, 10);
    if (selectedImage) {
      const updatedImages = imageList.map(img => {
        if (img.id === selectedImage.id) {
          return { ...img, sortOrder: newSortOrder };
        }
        if (img.sortOrder >= newSortOrder) {
          return { ...img, sortOrder: img.sortOrder + 1 };
        }
        return img;
      });
      setImageList(updatedImages);
      setSelectedImage({ ...selectedImage, sortOrder: newSortOrder });
    }
  };

  const handleDelete = () => {
    setIsConfirmationOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (selectedImage) {
      await deleteProductImage(productId, selectedImage.id);
      setIsConfirmationOpen(false);
      onImageUpdate();
    }
  };

  const handleUploadClick = () => {
    fileInputRef.current?.click();
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newFiles = Array.from(e.target.files || []);
    setStagedFiles(prevFiles => [...prevFiles, ...newFiles]);
  };

  const handleSaveUpload = async () => {
    if (stagedFiles.length > 0) {
      await uploadBulkProductImages(productId, stagedFiles);
      setStagedFiles([]);
      onImageUpdate();
    }
  };

  const handleCancelUpload = () => {
    setStagedFiles([]);
  };

  const handleRemoveStagedFile = (index: number) => {
    setStagedFiles(prevFiles => prevFiles.filter((_, i) => i !== index));
  };

  return (
    <>
      <div className={detailsStyles.infoSection}>
        <h3>{t('image_gallery')}</h3>
        <div className={detailsStyles.imageGalleryContainer}>
          {imageList.length > 0 && (
            <>
              <div className={detailsStyles.primaryImageContainer}>
                <img
                  src={selectedImage?.url}
                  alt={selectedImage?.altText || productName}
                  className={detailsStyles.primaryImage}
                />
              </div>
              <div className={detailsStyles.thumbnailContainer}>
                {imageList.map((img, index) => (
                  <img
                    key={index}
                    src={img.url}
                    alt={img.altText}
                    className={`${detailsStyles.thumbnail} ${selectedImage?.url === img.url ? detailsStyles.active : ''}`}
                    onClick={() => setSelectedImage(img)}
                  />
                ))}
              </div>
              {selectedImage && (
                <ImageActions
                  isPrimary={selectedImage.isPrimary}
                  sortOrder={selectedImage.sortOrder}
                  onSetPrimary={handleSetPrimary}
                  onSortOrderChange={handleSortOrderChange}
                  onSaveChanges={handleSaveChanges}
                  onDelete={handleDelete}
                />
              )}
            </>
          )}

          <div className={detailsStyles.uploadSection}>
            {stagedFiles.length > 0 && (
              <div>
                <ul className={detailsStyles.stagedFilesList}>
                  {stagedFiles.map((file, index) => (
                    <li key={index} className={detailsStyles.stagedFileItem}>
                      <span>{file.name}</span>
                      <button onClick={() => handleRemoveStagedFile(index)} className={detailsStyles.removeStagedFileBtn}>
                        &times;
                      </button>
                    </li>
                  ))}
                </ul>
                <div className={detailsStyles.imageActionGroup} style={{ marginTop: '1rem' }}>
                  <button onClick={handleSaveUpload} className={`${styles.adminButton} ${styles.add}`}>
                    {t('save_uploads')}
                  </button>
                  <button onClick={handleCancelUpload} className={`${styles.adminButton} ${styles.delete}`}>
                    {t('cancel')}
                  </button>
                </div>
              </div>
            )}
            <button onClick={handleUploadClick} className={`${styles.adminButton} ${styles.add}`} style={{ marginTop: '1rem' }}>
              {imageList.length > 0 ? t('upload_more_images') : t('upload_images')}
            </button>
            <input
              type="file"
              multiple
              ref={fileInputRef}
              onChange={handleFileChange}
              style={{ display: 'none' }}
            />
          </div>
        </div>
      </div>
      <ConfirmationModal
        isOpen={isConfirmationOpen}
        onClose={() => setIsConfirmationOpen(false)}
        onConfirm={handleConfirmDelete}
        message={t('delete_image_confirmation_message')}
      />
    </>
  );
};

export default ImageGallery;
