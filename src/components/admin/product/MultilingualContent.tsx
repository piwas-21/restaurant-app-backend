import React from 'react';
import { useTranslation } from 'react-i18next';
import { MultilingualContentProps, supportedLanguages } from './types';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';

export const MultilingualContent: React.FC<MultilingualContentProps> = ({
  register,
  errors,
  contentFields,
  appendContent,
  removeContent,
  watch,
  currentLanguage
}) => {
  const { t } = useTranslation();

  return (
    <div className={modalStyles.formGroup}>
      <h3>{t('multilingual_content')}</h3>
      {errors.content && <p className={modalStyles.errorMessage}>{errors.content.message}</p>}
      {contentFields.map((field, index) => (
        <div key={field.id} className={modalStyles.contentItem}>
          <button
            type="button"
            className={modalStyles.cancelButton}
            onClick={() => removeContent(index)}
          >
            {t('remove')}
          </button>
          <div className={styles.gridItem}>
            {(() => {
              const allContentItems = (watch('content') || []) as any[];
              const usedLanguages = allContentItems.map(item => item.language).filter(Boolean);
              const currentItemLanguage = (watch(`content.${index}.language`) as any) || '';

              return (
                <select {...register(`content.${index}.language`)}>
                  <option value="">{t('select_language')}</option>
                  {supportedLanguages.map(lang => {
                    const isUsedByMain = lang === currentLanguage;
                    const isUsedByOther = usedLanguages.includes(lang) && lang !== currentItemLanguage;
                    const isDisabled = isUsedByMain || isUsedByOther;

                    return (
                      <option
                        key={lang}
                        value={lang}
                        disabled={isDisabled}
                      >
                        {t(`lang_${lang}`)} {isUsedByMain ? '(Auto-added)' : ''}
                      </option>
                    );
                  })}
                </select>
              );
            })()}
            <input {...register(`content.${index}.name`)} placeholder={t('name_in_language')} />
            <textarea {...register(`content.${index}.description`)} placeholder={t('description_in_language')} />
          </div>
        </div>
      ))}
      <button
        type="button"
        className={`${styles.adminButton} ${modalStyles.addSectionButton}`}
        onClick={() => {
          const allContentItems = (watch('content') || []) as any[];
          const usedLanguages = allContentItems.map(item => item.language).filter(Boolean);
          const unavailableLanguages = [...usedLanguages, currentLanguage];
          const nextAvailableLanguage = supportedLanguages.find(lang => !unavailableLanguages.includes(lang)) || '';

          appendContent({ language: nextAvailableLanguage, name: '', description: '' });
        }}
      >
        {t('add_language_translation')}
      </button>
    </div>
  );
};
