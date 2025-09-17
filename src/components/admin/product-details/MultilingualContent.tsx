'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import detailsStyles from '@/app/styles/DetailsPage.module.css';

interface MultilingualContentProps {
  content: any;
}

const MultilingualContent: React.FC<MultilingualContentProps> = ({ content }) => {
  const { t } = useTranslation();

  if (!content || Object.keys(content).length === 0) {
    return null;
  }

  return (
    <div className={detailsStyles.infoSection}>
      <h3>{t('multilingual_content')}</h3>
      {Object.entries(content).map(([lang, data]: [string, any]) => (
        <div key={lang} className={detailsStyles.languageSection}>
          <h4>{t(`lang_${lang}`)}</h4>
          <p><strong>{t('name')}:</strong> {data.name}</p>
          <p><strong>{t('ingredients')}:</strong> {data.description}</p>
        </div>
      ))}
    </div>
  );
};

export default MultilingualContent;
