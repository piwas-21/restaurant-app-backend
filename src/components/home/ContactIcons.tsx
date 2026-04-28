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

  // wa.me requires the international number without the leading `+` and
  // with no separators. Stored numbers are E.164 (`+41227863333`), so a
  // single strip is enough; we don't try to normalise locally-formatted
  // numbers because backend validation already rejects those.
  const waPath = (e164: string) => e164.replace(/^\+/, '').replace(/\D/g, '');
  const waMessage = encodeURIComponent(t('whatsapp_default_message', 'Hello, I would like to make a reservation'));

  return (
    <nav className={styles.contactIcons} aria-label={t('home_contact_title', 'Get in touch')}>
      <h2 className={styles.heading}>{t('home_contact_title', 'Get in touch')}</h2>
      <ul className={styles.list}>
        {activePhones.map((p) => (
          <li key={p.id} className={styles.item}>
            {p.label && <span className={styles.label}>{p.label}</span>}
            <div className={styles.actions}>
              <a
                href={`tel:${p.number}`}
                className={styles.btn}
                aria-label={`${t('phone_label', 'Phone')}: ${p.number}`}
              >
                <Phone size={20} aria-hidden="true" />
                <span>{p.number}</span>
              </a>
              {p.whatsAppEnabled && (
                <a
                  href={`https://wa.me/${waPath(p.number)}?text=${waMessage}`}
                  className={styles.btnWhatsapp}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={`WhatsApp: ${p.number}`}
                >
                  <MessageCircle size={20} aria-hidden="true" />
                  <span>WhatsApp</span>
                </a>
              )}
            </div>
          </li>
        ))}
      </ul>
    </nav>
  );
}
