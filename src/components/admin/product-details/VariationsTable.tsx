'use client';

import React, { useState } from 'react';
import { Variation } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';
import ConfirmationModal from '@/components/common/ConfirmationModal';

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

  const startEdit = (idx: number) => { setEditingIndex(idx); setDraft({ ...local[idx] }); };
  const startAdd = () => { setAdding(true); setDraft({ name: '', priceModifier: 0, isActive: true, finalPrice: 0 }); };
  const cancel = () => { setEditingIndex(null); setAdding(false); setDraft(null); };
  const save = async () => {
    if (!draft) return;
    const next = [...local];
    if (adding) next.push(draft as Variation); else if (editingIndex!==null) next[editingIndex] = draft as Variation;
    try {
      if (productId && product) {
        const payload = buildProductPayload({ ...product, variations: next } as any);
        await updateProduct(productId, payload);
      }
      setLocal(next);
      cancel();
      onUpdated && onUpdated();
    } catch {}
  };
  const confirmDelete = (idx: number) => { setPendingDeleteIndex(idx); setConfirmOpen(true); };
  const handleConfirmDelete = async () => {
    if (pendingDeleteIndex===null) return;
    const idx = pendingDeleteIndex;
    setConfirmOpen(false);
    const next = local.filter((_,i)=>i!==idx);
    try {
      if (productId && product) {
        const payload = buildProductPayload({ ...product, variations: next } as any);
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
            <input placeholder={t('variation_name') as string} value={draft.name||''} onChange={e=>setDraft({...draft, name: e.target.value})} />
            <input placeholder={t('price_modifier') as string} type="number" step="0.01" value={draft.priceModifier as number} onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})} />
            <label><input type="checkbox" checked={!!draft.isActive} onChange={e=>setDraft({...draft, isActive: e.target.checked})} /> {t('active')}</label>
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
          <input placeholder={t('variation_name') as string} value={draft.name||''} onChange={e=>setDraft({...draft, name: e.target.value})} />
          <input placeholder={t('price_modifier') as string} type="number" step="0.01" value={draft.priceModifier as number} onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})} />
          <label><input type="checkbox" checked={!!draft.isActive} onChange={e=>setDraft({...draft, isActive: e.target.checked})} /> {t('active')}</label>
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
            <th>{t('price_modifier')}</th>
            <th>{t('final_price')}</th>
            <th>{t('status')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {local.map((v, idx) => (
            <tr key={v.id || v.name}>
              <td>
                {editingIndex===idx && draft ? (
                  <input value={draft.name||''} onChange={e=>setDraft({...draft, name: e.target.value})} />
                ) : v.name}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <input type="number" step="0.01" value={draft.priceModifier as number} onChange={e=>setDraft({...draft, priceModifier: parseFloat(e.target.value)})} />
                ) : `$${v.priceModifier.toFixed(2)}`}
              </td>
              <td>{`$${v.finalPrice.toFixed(2)}`}</td>
              <td>
                {editingIndex===idx && draft ? (
                  <label><input type="checkbox" checked={!!draft.isActive} onChange={e=>setDraft({...draft, isActive: e.target.checked})} /> {t('active')}</label>
                ) : (v.isActive ? t('active') : t('inactive'))}
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
