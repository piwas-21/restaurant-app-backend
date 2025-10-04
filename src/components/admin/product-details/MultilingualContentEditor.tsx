'use client';

import React, { useState } from 'react';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import styles from '@/app/styles/AdminPage.module.css';
import { useTranslation } from 'react-i18next';
import { updateProduct } from '@/services/productService';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import { buildProductPayload } from './buildProductPayload';
import ConfirmationModal from '@/components/common/ConfirmationModal';

const supportedLanguages = ["en", "tr", "es", "ar", "de", "fr", "it"];

interface Props {
  product: ProductDetails;
  onUpdated?: () => void;
}

const MultilingualContentEditor: React.FC<Props> = ({ product, onUpdated }) => {
  const { t } = useTranslation();
  const initial = Object.entries((product.content||{}) as any).map(([language, v]: any)=> ({ language, name: v.name||'', description: v.description||'' }));
  const [editing, setEditing] = useState(false);
  const [entries, setEntries] = useState(initial);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [pendingDeleteIndex, setPendingDeleteIndex] = useState<number | null>(null);

  const add = () => {
    const used = entries.map(e => e.language).filter(Boolean);
    const next = supportedLanguages.find(l => !used.includes(l)) || '';
    setEntries([...entries, { language: next, name: '', description: '' }]);
  };
  const askRemove = (idx: number) => { setPendingDeleteIndex(idx); setConfirmOpen(true); };
  const removeNow = () => {
    if (pendingDeleteIndex===null) return;
    setEntries(entries.filter((_,i)=>i!==pendingDeleteIndex));
    setPendingDeleteIndex(null);
    setConfirmOpen(false);
  };
  const save = async () => {
    const content = entries.reduce((acc: any, curr: any) => { if (curr.language) acc[curr.language] = { name: curr.name, description: curr.description }; return acc; }, {});
    const updated: ProductDetails = { ...product, content } as any;
    const payload = buildProductPayload(updated);
    await updateProduct(product.id, payload);
    setEditing(false);
    onUpdated && onUpdated();
  };

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h3>{t('multilingual_content')}</h3>
        {!editing ? (
          <button className={`${styles.adminButton} ${styles.edit}`} onClick={()=>setEditing(true)}>{t('edit')}</button>
        ) : (
          <div className={detailsStyles.actionRow}>
            <button className={`${styles.adminButton} ${styles.save}`} onClick={save}>{t('save')}</button>
            <button className={styles.cancelButton} onClick={()=>setEditing(false)}>{t('cancel')}</button>
          </div>
        )}
      </div>
      {!editing ? (
        initial.length === 0 ? (
          <p>{t('no_translations')}</p>
        ) : (
          <table className={detailsStyles.variationsTable}>
            <thead>
              <tr>
                <th>{t('language')}</th>
                <th>{t('name')}</th>
                <th>{t('description')} / {t('ingredients')}</th>
              </tr>
            </thead>
            <tbody>
              {initial.map((e:any)=> (
                <tr key={e.language}>
                  <td>{t(`lang_${e.language}`)}</td>
                  <td>{e.name}</td>
                  <td>{e.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      ) : (
        <div className={detailsStyles.formGrid}>
          {entries.map((e, idx) => (
            <div key={idx} className={detailsStyles.formGroup}>
              <div className={detailsStyles.actionRow}>
                <select value={e.language} onChange={ev=>setEntries(entries.map((x,i)=> i===idx ? { ...x, language: ev.target.value } : x))}>
                  <option value="" disabled>{t('language')}</option>
                  {supportedLanguages.map(l => (
                    <option key={l} value={l} disabled={entries.some((x,i)=> x.language === l && i !== idx)}>{t(`lang_${l}`)}</option>
                  ))}
                </select>
                <input placeholder={t('name_in_language') as string} value={e.name} onChange={ev=>setEntries(entries.map((x,i)=> i===idx ? { ...x, name: ev.target.value } : x))} />
                <input placeholder={t('ingredients_in_language') as string} value={e.description} onChange={ev=>setEntries(entries.map((x,i)=> i===idx ? { ...x, description: ev.target.value } : x))} />
                <button type="button" className={styles.deleteButton} onClick={()=>askRemove(idx)}>{t('remove')}</button>
              </div>
            </div>
          ))}
          <button type="button" className={`${styles.adminButton} ${styles.add}`} onClick={add}>{t('add_language_translation')}</button>
        </div>
      )}
      <ConfirmationModal isOpen={confirmOpen} onClose={()=>setConfirmOpen(false)} onConfirm={removeNow} message={t('delete_confirmation')} />
    </div>
  );
};

export default MultilingualContentEditor;
