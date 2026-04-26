"use client";

import React, { useEffect } from "react";
import Image from "next/image";
import styles from "./ImageModal.module.css";
import { getFullImageUrl } from "@/utils/image";

type Img = { url: string; alt?: string };

type Props = {
  isOpen: boolean;
  images: Img[];
  currentIndex: number;
  onClose: () => void;
  onNext: () => void;
  onPrev: () => void;
  altBase: string;
  onImageError?: () => void;
  previousLabel: string;
  nextLabel: string;
  closeLabel: string;
};

export default function ImageModal({ isOpen, images, currentIndex, onClose, onNext, onPrev, altBase, onImageError, previousLabel, nextLabel, closeLabel }: Props) {
  useEffect(() => {
    if (!isOpen) return;
    const handler = (event: KeyboardEvent) => {
      if (event.key === "ArrowRight" && images.length > 1) onNext();
      if (event.key === "ArrowLeft" && images.length > 1) onPrev();
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [isOpen, images.length, onNext, onPrev, onClose]);

  if (!isOpen || images.length === 0) return null;

  const active = images[currentIndex];

  return (
    <div className={styles.enlargedImageBackdrop} onClick={onClose}>
      <div className={styles.enlargedImageModalContainer} onClick={(e) => e.stopPropagation()}>
        <button className={styles.closeButtonModal} onClick={onClose} aria-label={closeLabel}>
          &times;
        </button>
        {images.length > 1 && (
          <button className={`${styles.navButtonModal} ${styles.prevButton}`} onClick={onPrev} aria-label={previousLabel}>
            &#10094;
          </button>
        )}
        <div className={styles.enlargedImageWrapper}>
          <Image
            src={getFullImageUrl(active.url)}
            alt={active.alt || `${altBase} - Image ${currentIndex + 1}`}
            fill
            quality={100}
            priority={true}
            sizes="100vw"
            className={styles.enlargedImageModal}
            onError={onImageError}
          />
        </div>
        {images.length > 1 && (
          <button className={`${styles.navButtonModal} ${styles.nextButton}`} onClick={onNext} aria-label={nextLabel}>
            &#10095;
          </button>
        )}
      </div>
    </div>
  );
}
