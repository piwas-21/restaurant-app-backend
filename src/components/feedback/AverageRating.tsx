// src/components/feedback/AverageRating.tsx
'use client';

import React from 'react';
import styles from '@/app/styles/FeedbackForm.module.css'; // Assuming shared styles or create a new one
import { useTranslation } from 'react-i18next';

interface AverageRatingProps {
  dishId: string;
  initialRatingData?: { average: number; count: number };
}

const AverageRating: React.FC<AverageRatingProps> = ({ dishId, initialRatingData }) => {
  const { t } = useTranslation();
  // In a real app, you might fetch this data or receive it as props after calculation
  const ratingData = initialRatingData;

  if (!ratingData || ratingData.count === 0) {
    return <p className={styles.averageRating}>{t('no_reviews_yet')}</p>;
  }

  return (
    <div
      className={styles.averageRatingContainer}
      aria-label={t('average_rating_label', { rating: ratingData.average, count: ratingData.count })}
    >
      <span className={styles.averageStars}>
        {'★'.repeat(Math.round(ratingData.average))}
        {'☆'.repeat(5 - Math.round(ratingData.average))}
      </span>
      <span className={styles.averageText}>
        {ratingData.average.toFixed(1)}/5 ({ratingData.count}{' '}
        {ratingData.count === 1 ? t('guest_singular') : t('guest_plural')})
      </span>
      {/* Using guest_singular/plural for review count as an example, ideally would have specific review count keys */}
    </div>
  );
};

export default AverageRating;
