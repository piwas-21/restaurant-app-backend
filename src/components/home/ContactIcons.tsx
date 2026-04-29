'use client';

import { Phone, MessageCircle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { RestaurantPhoneNumberDto } from '@/types/restaurantInfo';
import styles from './ContactIcons.module.css';

interface ContactIconsProps {
  phones: RestaurantPhoneNumberDto[];
}

export default function ContactIcons({ phones }: ContactIconsProps) {
  const { t } = useTranslation();
  const activePhones = phones
    .filter((p) => p.isActive)
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);

  if (activePhones.length === 0) return null;

  const waPath = (e164: string) => e164.replace(/^\+/, '').replace(/\D/g, '');
  const waMessage = encodeURIComponent(t('whatsapp_default_message', 'Hello, I would like to make a reservation'));

  const phoneNumbers = activePhones;
  const whatsappNumbers = activePhones.filter((p) => p.whatsAppEnabled);

  return (
    <div className={styles.fab} aria-label={t('home_contact_title', 'Get in touch')}>
      {whatsappNumbers.length > 0 && (
        <div className={styles.fabItem}>
          <div className={styles.numberList}>
            {whatsappNumbers.map((p) => (
              <a
                key={p.id}
                href={`https://wa.me/${waPath(p.number)}?text=${waMessage}`}
                className={styles.numberChip}
                target="_blank"
                rel="noopener noreferrer"
                aria-label={`WhatsApp: ${p.number}`}
              >
                {p.number}
              </a>
            ))}
          </div>
          <button className={`${styles.fabBtn} ${styles.fabBtnWhatsapp}`} aria-label="WhatsApp" tabIndex={-1}>
            <MessageCircle size={22} aria-hidden="true" />
          </button>
        </div>
      )}

      <div className={styles.fabItem}>
        <div className={styles.numberList}>
          {phoneNumbers.map((p) => (
            <a
              key={p.id}
              href={`tel:${p.number}`}
              className={styles.numberChip}
              aria-label={`${t('phone_label', 'Phone')}: ${p.number}`}
            >
              {p.number}
            </a>
          ))}
        </div>
        <button className={`${styles.fabBtn} ${styles.fabBtnPhone}`} aria-label={t('phone_label', 'Phone')} tabIndex={-1}>
          <Phone size={22} aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}
