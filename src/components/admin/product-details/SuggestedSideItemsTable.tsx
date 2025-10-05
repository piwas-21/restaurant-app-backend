'use client';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import { SideItem } from '@/app/admin/menu-management/interfaces';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';
import { updateProduct } from '@/services/productService';
import { getProducts } from '@/services/menuService';
import { buildProductPayload } from './buildProductPayload';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import { getCategories } from '@/services/categoryService';

interface Category { id: string; name: string; }

interface SuggestedSideItemsTableProps {
  suggestedSideItems: SideItem[];
  productId?: string;
  onUpdated?: () => void;
  product?: any;
}

const SuggestedSideItemsTable: React.FC<SuggestedSideItemsTableProps> = ({ suggestedSideItems, productId, onUpdated, product }) => {
  const { t } = useTranslation();
  const [local, setLocal] = useState<SideItem[]>(suggestedSideItems || []);
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<any[]>([]);
  const [showPicker, setShowPicker] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [draft, setDraft] = useState<Partial<SideItem> | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [pendingDeleteIndex, setPendingDeleteIndex] = useState<number | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);

  useEffect(() => {
    const fetchCategories = async () => {
      const resp = await getCategories();
      if (resp.success) {
        setCategories(resp.data.items);
      }
    };
    fetchCategories();
  }, []);

  useEffect(()=>{ setLocal(suggestedSideItems||[]); },[suggestedSideItems]);

  const runSearch = async () => {
    const resp = await getProducts(1, 20, undefined);
    if (resp.success) {
      const items = resp.data.items.filter((p:any)=> p.name.toLowerCase().includes(search.toLowerCase()));
      setResults(items);
    }
  };

  const toggleSelect = (id: string, checked: boolean) => {
    setSelectedIds(prev => checked ? Array.from(new Set([...(prev||[]), id])) : (prev||[]).filter(x=>x!==id));
  };

  const saveSelected = async () => {
    const additions = results.filter(r => selectedIds.includes(r.id)).map(p => ({ id: p.id, name: p.name, description: p.description, price: p.basePrice, isRequired: false } as SideItem));
    const next = [...local, ...additions.filter(a => !local.some(l => l.id === a.id))];
    if (productId && product) {
      const payload = buildProductPayload({ ...product, suggestedSideItems: next } as any, categories);
      await updateProduct(productId, payload);
    }
    setLocal(next);
    setShowPicker(false);
    setSelectedIds([]);
    onUpdated && onUpdated();
  };

  const startEdit = (idx: number) => { setEditingIndex(idx); setDraft({ ...local[idx] }); };
  const cancel = () => { setEditingIndex(null); setDraft(null); };
  const save = async () => {
    if (editingIndex===null || !draft) return;
    const next = [...local];
    next[editingIndex] = draft as SideItem;
    if (productId && product) {
      const payload = buildProductPayload({ ...product, suggestedSideItems: next } as any, categories);
      await updateProduct(productId, payload);
    }
    setLocal(next);
    cancel();
    onUpdated && onUpdated();
  };
  const confirmDelete = (idx: number) => { setPendingDeleteIndex(idx); setConfirmOpen(true); };
  const removeAt = async () => {
    if (pendingDeleteIndex===null) return;
    const idx = pendingDeleteIndex;
    setConfirmOpen(false);
    const next = local.filter((_,i)=>i!==idx);
    if (productId && product) {
      const payload = buildProductPayload({ ...product, suggestedSideItems: next } as any, categories);
      await updateProduct(productId, payload);
    }
    setLocal(next);
    onUpdated && onUpdated();
    setPendingDeleteIndex(null);
  };

  if (!suggestedSideItems || suggestedSideItems.length === 0) {
    return (
      <div className={detailsStyles.infoSection}>
        <div className={detailsStyles.headerRow}>
          <h3>{t('suggested_side_items')}</h3>
          <button className={`${styles.adminButton} ${styles.add}`} onClick={()=>setShowPicker(true)}>{t('add')}</button>
        </div>
        {showPicker && (
          <div className={detailsStyles.formGrid}>
            <input
              placeholder={t('search') as string}
              value={search}
              onChange={e=>setSearch(e.target.value)}
              onKeyPress={(e) => {
                if (e.key === 'Enter' && search.trim()) {
                  runSearch();
                }
              }}
            />

            <div className={modalStyles.chipGroup}>
              {results.map(p => {
                const id = `side-item-result-${p.id}`;
                const checked = selectedIds.includes(p.id);
                return (
                  <div key={p.id} className={modalStyles.chip}>
                    <input type="checkbox" id={id} checked={checked} onChange={e=>toggleSelect(p.id, e.target.checked)} />
                    <label htmlFor={id}>{p.name}</label>
                  </div>
                );
              })}
            </div>
            <div className={detailsStyles.actionRow}>
              <button className={`${styles.adminButton}`} onClick={runSearch}>{t('search')}</button>
              <button className={styles.cancelButton} onClick={()=>{ setShowPicker(false); setSelectedIds([]); }}>{t('cancel')}</button>
              <button className={`${styles.adminButton} ${styles.save}`} onClick={saveSelected} disabled={selectedIds.length===0}>{t('save')}</button>
            </div>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h3>{t('suggested_side_items')}</h3>
        <button className={`${styles.adminButton} ${styles.add}`} onClick={()=>setShowPicker(true)}>{t('add')}</button>
      </div>
      {showPicker && (
        <div className={detailsStyles.formGrid}>
          <input
            placeholder={t('search') as string}
            value={search}
            onChange={e=>setSearch(e.target.value)}
            onKeyPress={(e) => {
              if (e.key === 'Enter' && search.trim()) {
                runSearch();
              }
            }}
          />

          <div className={modalStyles.chipGroup}>
            {results.map(p => {
              const id = `side-item-result-${p.id}`;
              const checked = selectedIds.includes(p.id);
              return (
                <div key={p.id} className={modalStyles.chip}>
                  <input type="checkbox" id={id} checked={checked} onChange={e=>toggleSelect(p.id, e.target.checked)} />
                  <label htmlFor={id}>{p.name}</label>
                </div>
              );
            })}
          </div>

          <div className={detailsStyles.actionRow}>
            <button className={`${styles.adminButton}`} onClick={runSearch}>{t('search')}</button>
            <button className={styles.cancelButton} onClick={()=>{ setShowPicker(false); setSelectedIds([]); }}>{t('cancel')}</button>
            <button className={`${styles.adminButton} ${styles.save}`} onClick={saveSelected} disabled={selectedIds.length===0}>{t('save')}</button>
          </div>
        </div>
      )}
      <table className={detailsStyles.variationsTable}>
        <thead>
          <tr>
            <th>{t('item_name')}</th>
            <th>{t('price')}</th>
            <th>{t('required')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {local.map((item, idx) => (
            <tr key={item.id}>
              <td>
                {editingIndex===idx && draft ? (
                  <input value={draft.name||''} onChange={e=>setDraft({...draft, name: e.target.value})} />
                ) : item.name}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <input type="number" step="0.01" value={draft.price as number} onChange={e=>setDraft({...draft, price: parseFloat(e.target.value)})} />
                ) : `$${item.price.toFixed(2)}`}
              </td>
              <td>
                {editingIndex===idx && draft ? (
                  <label><input type="checkbox" checked={!!draft.isRequired} onChange={e=>setDraft({...draft, isRequired: e.target.checked})} /> {t('required')}</label>
                ) : (item.isRequired ? t('yes') : t('no'))}
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
                    <button className={styles.deleteButton} onClick={()=>confirmDelete(idx)}>{t('remove')}</button>
                    <Link href={`/admin/menu-management/${item.id}`} className={`${styles.adminButton} ${styles.details}`}>{t('details')}</Link>
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <ConfirmationModal isOpen={confirmOpen} onClose={()=>setConfirmOpen(false)} onConfirm={removeAt} message={t('delete_confirmation')} />
    </div>
  );
};

export default SuggestedSideItemsTable;
