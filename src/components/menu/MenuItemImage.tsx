"use client";

import React from "react";
import Image from "next/image";
import styles from "./MenuItemImage.module.css";
import { getFullImageUrl } from "@/utils/image";

type Props = {
  imageUrl: string;
  alt: string;
  imageCount?: number;
  countLabel?: string;
  onClick: () => void;
  onError?: () => void;
};

export default function MenuItemImage({ imageUrl, alt, imageCount, countLabel, onClick, onError }: Props) {
  const fullUrl = getFullImageUrl(imageUrl);
  return (
    <div className={styles.itemImageContainer} onClick={onClick} style={{ cursor: "pointer" }}>
      <Image
        src={fullUrl}
        alt={alt}
        fill
        sizes="(max-width: 768px) 100vw, (max-width: 1200px) 50vw, 33vw"
        className={styles.itemImage}
        priority={false}
        quality={100}
        loading="eager"
        onError={onError}
      />
      {imageCount && imageCount > 1 && (
        <span className={styles.imageCount}>
          {imageCount} {countLabel}
        </span>
      )}
    </div>
  );
}
