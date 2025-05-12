// src/components/feedback/FeedbackForm.tsx
"use client";

import React, { useState } from 'react';
import styles from '@/app/styles/FeedbackForm.module.css';
import { useTranslation } from 'react-i18next';

interface FeedbackFormProps {
  dishId: string;
  onSubmitSuccess: () => void;
}

const FeedbackForm: React.FC<FeedbackFormProps> = ({ dishId, onSubmitSuccess }) => {
  const { t } = useTranslation();
  const [rating, setRating] = useState(0); // Actual selected rating
  const [hoverRating, setHoverRating] = useState(0); // Rating based on hover
  const [comment, setComment] = useState('');
  const [name, setName] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleRatingClick = (newRating: number) => {
    setRating(newRating);
    setError(null); // Clear error when rating is selected
  };

  const handleStarMouseEnter = (starValue: number) => {
    setHoverRating(starValue);
  };

  const handleStarMouseLeave = () => {
    setHoverRating(0);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    setError(null);

    if (rating === 0) {
      setError(t('feedback_form_error_rating_required'));
      setIsSubmitting(false);
      return;
    }

    console.log('Submitting feedback:', { dishId, rating, comment, name });
    await new Promise(resolve => setTimeout(resolve, 1000)); // Simulate API call

    setIsSubmitting(false);
    setRating(0); // Reset rating to 0 after successful submission
    setHoverRating(0); // Reset hover rating
    setComment('');
    setName('');
    onSubmitSuccess();
    alert(t('feedback_form_submit_success_message'));
  };

  return (
    <form onSubmit={handleSubmit} className={styles.feedbackForm} aria-labelledby="feedback-form-heading">
      <h3 id="feedback-form-heading" className="sr-only">{t('feedback_form_heading')}</h3>
      
      <div className={styles.formGroup} role="group" aria-labelledby="rating-label">
        <label id="rating-label" className={styles.ratingLabel}>{t('feedback_form_rating_label')}:</label>
        <div className={styles.starRating} onMouseLeave={handleStarMouseLeave}>
          {[1, 2, 3, 4, 5].map((starValue) => {
            const isFilled = starValue <= (hoverRating || rating);
            return (
              <button
                key={starValue}
                type="button"
                className={isFilled ? styles.starFilled : styles.starEmpty} // Keep for potential styling (e.g., color)
                onClick={() => handleRatingClick(starValue)}
                onMouseEnter={() => handleStarMouseEnter(starValue)}
                aria-label={t('feedback_form_star_rating_aria_label', { count: starValue })}
                aria-pressed={starValue === rating}
              >
                {isFilled ? '★' : '☆'} 
              </button>
            );
          })}
        </div>
      </div>

      <div className={styles.formGroup}>
        <label htmlFor="comment">{t('feedback_form_comment_label')}:</label>
        <textarea
          id="comment"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          className={styles.textareaInput}
          placeholder={t('feedback_form_comment_placeholder')}
          rows={4}
        />
      </div>

      <div className={styles.formGroup}>
        <label htmlFor="name">{t('feedback_form_name_label')} ({t('feedback_form_optional_label')}):</label>
        <input
          type="text"
          id="name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          className={styles.textInput}
          placeholder={t('feedback_form_name_placeholder')}
        />
      </div>

      {error && <p className={styles.errorMessage} role="alert">{error}</p>}

      <button type="submit" className={styles.submitButton} disabled={isSubmitting}>
        {isSubmitting ? t('feedback_form_submitting_button') : t('feedback_form_submit_button')}
      </button>
    </form>
  );
};

export default FeedbackForm;
