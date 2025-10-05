'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Variation } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import { getCategories } from '@/services/categoryService';

interface Category { id: string; name: string; }

interface VariationsTableProps {
  variations: Variation[];
  productId?: string;
  onUpdated?: () => void;
  product?: any;
}

const VariationsTable: React.FC<VariationsTableProps> = ({ variations, productId, onUpdated, product }) => {
  const { t } = useTranslation();
  const [local, setLocal] = useState<Variation[]>(variations || []);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState<Partial<Variation> | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [pendingDeleteIndex, setPendingDeleteIndex] = useState<number | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [nameFieldError, setNameFieldError] = useState(false);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const editNameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const fetchCategories = async () => {
      const resp = await getCategories();
      if (resp.success) {
        setCategories(resp.data.items);
      }
    };
    fetchCategories();
  }, []);

  const updateDraftName = (value: string) => {
    setDraft({...draft, name: value});
    // Clear validation error when user starts typing a valid name
    if (value.trim() && nameFieldError) {
      setNameFieldError(false);
    }
  };
  const startEdit = (idx: number) => { setEditingIndex(idx); setDraft({ ...local[idx] }); setNameFieldError(false); };
  const startAdd = () => { setAdding(true); setDraft({ name: '', description: '', priceModifier: 0, isActive: true, displayOrder: 0 }); setNameFieldError(false); };
  const save = async () => {
    if (!draft) return;

    // Validate required name field
    if (!draft.name?.trim()) {
      setNameFieldError(true);
      // Focus the appropriate input field
      if (adding && nameInputRef.current) {
        nameInputRef.current.focus();
      } else if (editingIndex !== null && editNameInputRef.current) {
        editNameInputRef.current.focus();
      }
      return;
    }

    setNameFieldError(false);

    const next = [...local];
    if (adding) next.push(draft as Variation); else if (editingIndex!==null) next[editingIndex] = draft as Variation;
    try {
      if (productId && product) {
        const payload = buildProductPayload({ ...product, variations: next } as any, categories);
        await updateProduct(productId, payload);
      }
      setLocal(next);
      cancel();
      onUpdated && onUpdated();
    } catch {}
  };
  const cancel = () => { setEditingIndex(null); setAdding(false); setDraft(null); setNameFieldError(false); };
  const confirmDelete = (idx: number) => { setPendingDeleteIndex(idx); setConfirmOpen(true); };
  const handleConfirmDelete = async () => {
    if (pendingDeleteIndex===null) return;
    const idx = pendingDeleteIndex;
    setConfirmOpen(false);
    const next = local.filter((_,i)=>i!==idx);
    try {
      if (productId && product) {
        const payload = buildProductPayload({ ...product, variations: next } as any, categories);
        await updateProduct(productId, payload);
      }
      setLocal(next);
      onUpdated && onUpdated();
    } catch {}
    setPendingDeleteIndex(null);
  };

  if (!variations || variations.length === 0) {
    return (
      <div className={detailsStyles.infoSection}>
        <div className={detailsStyles.headerRow}>
          <h3>{t('variations')}</h3>
          <button className={`${styles.adminButton} ${styles.add}`} onClick={startAdd}>{t('add')}</button>
        </div>
        {adding && draft && (
          <div className={detailsStyles.formGrid}>
            <div className={detailsStyles.formGroup}>
              <label>{t('variation_name')}</label>
              <input
                ref={nameInputRef}
                placeholder={t('variation_name') as string}
                value={draft.name||''}
                onChange={e=>updateDraftName(e.target.value)}
                className={nameFieldError ? modalStyles.fieldError : ''}
              />
            </div>
            <div className={detailsStyles.formGroup}>
              <label>{t('description')}</label>
              <input
                placeholder={t('description') as string}
                value={draft.description||''}
                onChange={e=>setDraft({...draft, description: e.target.value})}
              />
            </div>
            <div className={detailsStyles.formGroup}>
              <label>{t('price_modifier')}</label>
              <input
                placeholder={t('price_modifier') as string}
                type="number"
                step="0.01"
                value={draft.priceModifier as number}
                onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})}
              />
            </div>
            <div className={detailsStyles.formGroup}>
              <label>{t('display_order')}</label>
              <input
                placeholder={t('display_order') as string}
                type="number"
                value={draft.displayOrder as number}
                onChange={e=>setDraft({...draft, displayOrder: parseInt(e.target.value)})}
              />
            </div>
            <div className={detailsStyles.formGroup}>
              <label>{t('status')}</label>
              <div className={modalStyles.chipGroup}>
                <div className={modalStyles.chip}>
                  <input
                    type="checkbox"
                    id="variation-active-add"
                    checked={draft.isActive}
                    onChange={e => setDraft({...draft, isActive: e.target.checked})}
                  />
                  <label htmlFor="variation-active-add">{t('active')}</label>
                </div>
              </div>
            </div>
            <div className={detailsStyles.actionRow}>
              <button className={`${styles.adminButton} ${styles.save}`} onClick={save}>{t('save')}</button>
              <button className={styles.cancelButton} onClick={cancel}>{t('cancel')}</button>
            </div>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h3>{t('variations')}</h3>
        <button className={`${styles.adminButton} ${styles.add}`} onClick={startAdd}>{t('add')}</button>
      </div>
      {adding && draft && (
        <div className={detailsStyles.formGrid}>
          <div className={detailsStyles.formGroup}>
            <label>{t('variation_name')}</label>
            <input
              ref={nameInputRef}
              placeholder={t('variation_name') as string}
              value={draft.name||''}
              onChange={e=>updateDraftName(e.target.value)}
              className={nameFieldError ? modalStyles.fieldError : ''}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('description')}</label>
            <input
              placeholder={t('description') as string}
              value={draft.description||''}
              onChange={e=>setDraft({...draft, description: e.target.value})}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('price_modifier')}</label>
            <input
              placeholder={t('price_modifier') as string}
              type="number"
              step="0.01"
              value={draft.priceModifier as number}
              onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('display_order')}</label>
            <input
              placeholder={t('display_order') as string}
              type="number"
              value={draft.displayOrder as number}
              onChange={e=>setDraft({...draft, displayOrder: parseInt(e.target.value)})}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('status')}</label>
            <div className={modalStyles.chipGroup}>
              <div className={modalStyles.chip}>
                <input
                  type="checkbox"
                  id="variation-active-main-add"
                  checked={draft.isActive}
                  onChange={e => setDraft({...draft, isActive: e.target.checked})}
                />
                <label htmlFor="variation-active-main-add">{t('active')}</label>
              </div>
            </div>
          </div>
          <div className={detailsStyles.actionRow}>
            <button className={`${styles.adminButton} ${styles.save}`} onClick={save}>{t('save')}</button>
            <button className={styles.cancelButton} onClick={cancel}>{t('cancel')}</button>
          </div>
        </div>
      )}
      <table className={detailsStyles.variationsTable}>
        <thead>
          <tr>
            <th>{t('variation_name')}</th>
            <th>{t('description')}</th>
            <th>{t('price_modifier')}</th>
            <th>{t('display_order')}</th>
            <th>{t('status')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {local.map((v, idx) => (
            <tr key={v.id || v.name} className={!v.isActive ? detailsStyles.inactiveRow : ''}>
              <td>
                {editingIndex===idx && draft ? (
                  <input
                    ref={editNameInputRef}
                    value={draft.name||''}
                    onChange={e=>updateDraftName(e.target.value)}
                    className={nameFieldError ? modalStyles.fieldError : ''}
                  />
                ) : v.name}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <input value={draft.description||''} onChange={e=>setDraft({...draft, description: e.target.value})} />
                ) : (v.description || '-')}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <input type="number" step="0.01" value={draft.priceModifier as number} onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})} />
                ) : `CHF${v.priceModifier.toFixed(2)}`}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <input type="number" value={draft.displayOrder as number} onChange={e=>setDraft({...draft, displayOrder: parseInt(e.target.value)})} />
                ) : (v.displayOrder || 0)}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <div className={modalStyles.chipGroup}>
                    <div className={modalStyles.chip}>
                      <input
                        type="checkbox"
                        id={`variation-active-edit-${idx}`}
                        checked={draft.isActive}
                        onChange={e => setDraft({...draft, isActive: e.target.checked})}
                      />
                      <label htmlFor={`variation-active-edit-${idx}`}>{t('active')}</label>
                    </div>
                  </div>
                ) : (
                  <span className={`${detailsStyles.statusBadge} ${v.isActive ? detailsStyles.statusActive : detailsStyles.statusInactive}`}>
                    {v.isActive ? t('active') : t('inactive')}
                  </span>
                )}
              </td>
              <td>
                {editingIndex===idx ? (
                  <div className={detailsStyles.actionRow}>
                    <button className={`${styles.adminButton} ${styles.save}`} onClick={save}>{t('save')}</button>
                    <button className={styles.cancelButton} onClick={cancel}>{t('cancel')}</button>
                  </div>
                ) : (
                  <div className={detailsStyles.actionRow}>
                    <button className={`${styles.adminButton} ${styles.edit}`} onClick={()=>startEdit(idx)}>{t('edit')}</button>
                    <button className={styles.deleteButton} onClick={()=>confirmDelete(idx)}>{t('delete')}</button>
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <ConfirmationModal isOpen={confirmOpen} onClose={()=>setConfirmOpen(false)} onConfirm={handleConfirmDelete} message={t('delete_confirmation')} />
    </div>
  );
};

export default VariationsTable;
