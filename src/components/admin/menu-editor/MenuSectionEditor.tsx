'use client';

import React, { useState } from 'react';
import styles from './MenuEditor.module.css';
import { MenuSection, MenuSectionItem } from '@/types/menu';
import MenuItemSelector from './MenuItemSelector';
import { useTranslation } from 'react-i18next';

interface MenuSectionEditorProps {
  sections: MenuSection[];
  onChange: (sections: MenuSection[]) => void;
}

const MenuSectionEditor: React.FC<MenuSectionEditorProps> = ({
  sections,
  onChange,
}) => {
  const { t } = useTranslation();
  const [expandedSections, setExpandedSections] = useState<Set<string>>(new Set());

  const toggleSection = (sectionId: string) => {
    const newExpanded = new Set(expandedSections);
    if (newExpanded.has(sectionId)) {
      newExpanded.delete(sectionId);
    } else {
      newExpanded.add(sectionId);
    }
    setExpandedSections(newExpanded);
  };

  const addSection = () => {
    const newSection: MenuSection = {
      id: `temp-${Date.now()}`,
      name: '',
      description: '',
      displayOrder: sections.length,
      isRequired: true,
      minSelection: 1,
      maxSelection: 1,
      items: [],
    };
    onChange([...sections, newSection]);
    setExpandedSections(new Set([...expandedSections, newSection.id]));
  };

  const updateSection = (index: number, updates: Partial<MenuSection>) => {
    const newSections = [...sections];
    newSections[index] = { ...newSections[index], ...updates };
    onChange(newSections);
  };

  const removeSection = (index: number) => {
    const newSections = sections.filter((_, i) => i !== index);
    onChange(newSections);
  };

  const moveSection = (index: number, direction: 'up' | 'down') => {
    if (
      (direction === 'up' && index === 0) ||
      (direction === 'down' && index === sections.length - 1)
    ) {
      return;
    }

    const newSections = [...sections];
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    [newSections[index], newSections[targetIndex]] = [
      newSections[targetIndex],
      newSections[index],
    ];

    // Update display orders
    newSections.forEach((section, i) => {
      section.displayOrder = i;
    });

    onChange(newSections);
  };

  const updateSectionItems = (index: number, items: MenuSectionItem[]) => {
    updateSection(index, { items });
  };

  return (
    <div className={styles.scheduleEditor}>
      <div className={styles.sectionHeader}>
        <h3 className={styles.sectionTitle}>{t('menu_sections')}</h3>
        <button onClick={addSection} className={styles.addButton}>
          + {t('add_section')}
        </button>
      </div>

      {sections.length === 0 ? (
        <div className={styles.emptyState}>
          <p>{t('no_sections_yet')}</p>
          <p className={styles.helpText}>
            {t('sections_help')}
          </p>
        </div>
      ) : (
        <div className={styles.sectionList}>
          {sections.map((section, index) => (
            <div key={section.id} className={styles.sectionCard}>
              <div className={styles.sectionHeader}>
                <div className={styles.sectionHeaderLeft}>
                  <span className={styles.dragHandle}>⋮⋮</span>
                  <h4 className={styles.sectionName}>
                    {section.name || `${t('section')} ${index + 1}`}
                  </h4>
                </div>
                <div className={styles.sectionActions}>
                  <button
                    onClick={() => moveSection(index, 'up')}
                    disabled={index === 0}
                    className={styles.iconButton}
                    title={t('move_up')}
                  >
                    ↑
                  </button>
                  <button
                    onClick={() => moveSection(index, 'down')}
                    disabled={index === sections.length - 1}
                    className={styles.iconButton}
                    title={t('move_down')}
                  >
                    ↓
                  </button>
                  <button
                    onClick={() => toggleSection(section.id)}
                    className={styles.iconButton}
                    title={expandedSections.has(section.id) ? t('collapse') : t('expand')}
                  >
                    {expandedSections.has(section.id) ? '−' : '+'}
                  </button>
                  <button
                    onClick={() => removeSection(index)}
                    className={`${styles.iconButton} ${styles.danger}`}
                    title={t('delete_section')}
                  >
                    ×
                  </button>
                </div>
              </div>

              {expandedSections.has(section.id) && (
                <div className={styles.sectionForm}>
                  {/* Section Name */}
                  <div className={styles.formGroup}>
                    <label>{t('section_name')} *</label>
                    <input
                      type="text"
                      value={section.name}
                      onChange={(e) => updateSection(index, { name: e.target.value })}
                      placeholder={t('section_name_placeholder')}
                      className={styles.input}
                    />
                  </div>

                  {/* Section Description */}
                  <div className={styles.formGroup}>
                    <label>{t('description')}</label>
                    <textarea
                      value={section.description || ''}
                      onChange={(e) =>
                        updateSection(index, { description: e.target.value })
                      }
                      placeholder={t('description_placeholder')}
                      className={`${styles.input} ${styles.textarea}`}
                    />
                  </div>

                  {/* Required Toggle & Selection Limits */}
                  <div className={styles.formRow}>
                    <div className={styles.formGroup}>
                      <label className={styles.toggleLabel}>
                        <input
                          type="checkbox"
                          checked={section.isRequired}
                          onChange={(e) =>
                            updateSection(index, { isRequired: e.target.checked })
                          }
                          className={styles.checkbox}
                        />
                        <span>{t('required_section')}</span>
                      </label>
                    </div>
                  </div>

                  <div className={styles.formRow}>
                    <div className={styles.formGroup}>
                      <label>{t('minimum_selection')}</label>
                      <input
                        type="number"
                        min="0"
                        value={section.minSelection}
                        onChange={(e) =>
                          updateSection(index, {
                            minSelection: parseInt(e.target.value) || 0,
                          })
                        }
                        className={`${styles.input} ${styles.numberInput}`}
                      />
                    </div>

                    <div className={styles.formGroup}>
                      <label>{t('maximum_selection')}</label>
                      <input
                        type="number"
                        min="1"
                        value={section.maxSelection}
                        onChange={(e) =>
                          updateSection(index, {
                            maxSelection: parseInt(e.target.value) || 1,
                          })
                        }
                        className={`${styles.input} ${styles.numberInput}`}
                      />
                    </div>
                  </div>

                  {/* Menu Items */}
                  <MenuItemSelector
                    items={section.items}
                    onChange={(items) => updateSectionItems(index, items)}
                  />
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default MenuSectionEditor;
