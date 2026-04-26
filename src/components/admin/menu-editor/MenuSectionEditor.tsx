'use client';

import React, { useState, useEffect } from 'react';
import styles from './MenuEditor.module.css';
import { MenuSection, MenuSectionItem } from '@/types/menu';
import MenuItemSelector from './MenuItemSelector';
import { useTranslation } from 'react-i18next';
import ConfirmationModal from '@/components/common/ConfirmationModal';

interface MenuSectionEditorProps {
  sections: MenuSection[];
  onChange: (sections: MenuSection[]) => void;
}

const MenuSectionEditor: React.FC<MenuSectionEditorProps> = ({
  sections,
  onChange,
}) => {
  const { t } = useTranslation();
  const [localSections, setLocalSections] = useState<MenuSection[]>(sections);
  const [hasChanges, setHasChanges] = useState(false);
  const [expandedSections, setExpandedSections] = useState<Set<string>>(new Set());
  const [sectionToDelete, setSectionToDelete] = useState<number | null>(null);
  const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

  // Reset local state when sections prop changes
  useEffect(() => {
    setLocalSections(sections);
    setHasChanges(false);
  }, [sections]);

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
      displayOrder: 0, // New section goes to top
      isRequired: true,
      minSelection: 1,
      maxSelection: 1,
      items: [],
    };

    // Update display orders for existing sections
    const updatedSections = localSections.map(s => ({
      ...s,
      displayOrder: s.displayOrder + 1
    }));

    // Add new section at the beginning
    setLocalSections([newSection, ...updatedSections]);
    setExpandedSections(new Set([...expandedSections, newSection.id]));
    setHasChanges(true);
  };

  const updateSection = (index: number, updates: Partial<MenuSection>) => {
    const newSections = [...localSections];
    newSections[index] = { ...newSections[index], ...updates };
    setLocalSections(newSections);
    setHasChanges(true);
  };

  const confirmRemoveSection = (index: number) => {
    setSectionToDelete(index);
  };

  const handleRemoveSection = () => {
    if (sectionToDelete !== null) {
      const newSections = localSections.filter((_, i) => i !== sectionToDelete);
      setLocalSections(newSections);
      setSectionToDelete(null);
      setHasChanges(true);
    }
  };

  const moveSection = (index: number, direction: 'up' | 'down') => {
    if (
      (direction === 'up' && index === 0) ||
      (direction === 'down' && index === localSections.length - 1)
    ) {
      return;
    }

    const newSections = [...localSections];
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    [newSections[index], newSections[targetIndex]] = [
      newSections[targetIndex],
      newSections[index],
    ];

    // Update display orders
    newSections.forEach((section, i) => {
      section.displayOrder = i;
    });

    setLocalSections(newSections);
    setHasChanges(true);
  };

  const handleDragStart = (index: number) => {
    setDraggedIndex(index);
  };

  const handleDragOver = (e: React.DragEvent, index: number) => {
    e.preventDefault();

    if (draggedIndex === null || draggedIndex === index) return;

    const newSections = [...localSections];
    const draggedSection = newSections[draggedIndex];

    // Remove from old position
    newSections.splice(draggedIndex, 1);
    // Insert at new position
    newSections.splice(index, 0, draggedSection);

    // Update display orders
    newSections.forEach((section, i) => {
      section.displayOrder = i;
    });

    setLocalSections(newSections);
    setDraggedIndex(index);
    setHasChanges(true);
  };

  const handleDragEnd = () => {
    setDraggedIndex(null);
  };

  const updateSectionItems = (index: number, items: MenuSectionItem[]) => {
    updateSection(index, { items });
  };

  const handleSave = () => {
    onChange(localSections);
    setHasChanges(false);
  };

  const handleCancel = () => {
    setLocalSections(sections);
    setHasChanges(false);
  };

  return (
    <div className={styles.scheduleEditor}>
      <div className={styles.sectionHeader}>
        <h3 className={styles.sectionTitle}>{t('menu_sections')}</h3>
        <button type="button" onClick={addSection} className={styles.addButton}>
          + {t('add_section')}
        </button>
      </div>

      {localSections.length === 0 ? (
        <div className={styles.emptyState}>
          <p>{t('no_sections_yet')}</p>
          <p className={styles.helpText}>
            {t('sections_help')}
          </p>
        </div>
      ) : (
        <div className={styles.sectionList}>
          {localSections.map((section, index) => (
            <div
              key={section.id}
              className={styles.sectionCard}
              draggable
              onDragStart={() => handleDragStart(index)}
              onDragOver={(e) => handleDragOver(e, index)}
              onDragEnd={handleDragEnd}
              style={{ opacity: draggedIndex === index ? 0.5 : 1 }}
            >
              <div className={styles.sectionHeader}>
                <div className={styles.sectionHeaderLeft}>
                  <span className={styles.dragHandle}>⋮⋮</span>
                  <h4 className={styles.sectionName}>
                    {section.name || `${t('section')} ${index + 1}`}
                  </h4>
                </div>
                <div className={styles.sectionActions}>
                  <button
                    type="button"
                    onClick={() => moveSection(index, 'up')}
                    disabled={index === 0}
                    className={styles.iconButton}
                    title={t('move_up')}
                  >
                    ↑
                  </button>
                  <button
                    type="button"
                    onClick={() => moveSection(index, 'down')}
                    disabled={index === localSections.length - 1}
                    className={styles.iconButton}
                    title={t('move_down')}
                  >
                    ↓
                  </button>
                  <button
                    type="button"
                    onClick={() => toggleSection(section.id)}
                    className={styles.iconButton}
                    title={expandedSections.has(section.id) ? t('collapse') : t('expand')}
                  >
                    {expandedSections.has(section.id) ? '−' : '+'}
                  </button>
                  <button
                    type="button"
                    onClick={() => confirmRemoveSection(index)}
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

                  {/* Required Toggle as Chip */}
                  <div className={styles.formRow}>
                    <div className={styles.chipGroup}>
                      <div className={styles.chip}>
                        <input
                          type="checkbox"
                          id={`required-${section.id}`}
                          checked={section.isRequired}
                          onChange={(e) =>
                            updateSection(index, { isRequired: e.target.checked })
                          }
                        />
                        <label htmlFor={`required-${section.id}`}>
                          {t('required_section')}
                        </label>
                      </div>
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
                    maxSelection={section.maxSelection}
                    onChange={(items) => updateSectionItems(index, items)}
                  />
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Save/Cancel Buttons */}
      {hasChanges && (
        <div className={styles.editorActions}>
          <button type="button" onClick={handleCancel} className={styles.cancelButton}>
            {t('cancel')}
          </button>
          <button type="button" onClick={handleSave} className={styles.saveButton}>
            {t('save')}
          </button>
        </div>
      )}

      <ConfirmationModal
        isOpen={sectionToDelete !== null}
        onClose={() => setSectionToDelete(null)}
        onConfirm={handleRemoveSection}
        message={t('confirm_delete_section', 'Are you sure you want to delete this section?')}
      />
    </div>
  );
};

export default MenuSectionEditor;
