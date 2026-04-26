"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';

export default function PrivacyPolicyPage() {
  const { t } = useTranslation();

  return (
    <div className="container mx-auto px-4 py-8 max-w-4xl">
      <h1 className="text-3xl font-bold mb-6">{t('privacy_policy.title', 'Privacy Policy')}</h1>

      <div className="prose dark:prose-invert max-w-none">
        <p className="mb-4">{t('privacy_policy.last_updated', { date: new Date().toLocaleDateString() })}</p>

        <section className="mb-6">
          <h2 className="text-2xl font-semibold mb-3">{t('privacy_policy.intro_title', '1. Introduction')}</h2>
          <p>
            {t('privacy_policy.intro_text')}
          </p>
        </section>

        <section className="mb-6">
          <h2 className="text-2xl font-semibold mb-3">{t('privacy_policy.info_collect_title', '2. Information We Collect')}</h2>
          <p>
            {t('privacy_policy.info_collect_text')}
          </p>
          <ul className="list-disc pl-6 mt-2">
            <li>{t('privacy_policy.info_collect_list_1', 'Name and Contact Data')}</li>
            <li>{t('privacy_policy.info_collect_list_2', 'Credentials')}</li>
            <li>{t('privacy_policy.info_collect_list_3', 'Payment Data')}</li>
          </ul>
        </section>

        <section className="mb-6">
          <h2 className="text-2xl font-semibold mb-3">{t('privacy_policy.how_use_title', '3. How We Use Your Information')}</h2>
          <p>
            {t('privacy_policy.how_use_text')}
          </p>
          <ul className="list-disc pl-6 mt-2">
            <li>{t('privacy_policy.how_use_list_1', 'To facilitate account creation and logon process.')}</li>
            <li>{t('privacy_policy.how_use_list_2', 'To send you marketing and promotional communications.')}</li>
            <li>{t('privacy_policy.how_use_list_3', 'To fulfill and manage your orders.')}</li>
          </ul>
        </section>

        <section className="mb-6">
          <h2 className="text-2xl font-semibold mb-3">{t('privacy_policy.contact_title', '4. Contact Us')}</h2>
          <p>
            {t('privacy_policy.contact_text')}
          </p>
          <address className="mt-2 not-italic">
            RUMI Restaurant<br />
            Rue du Grand-Pré 45<br />
            1202 Genève, Switzerland
          </address>
        </section>
      </div>
    </div>
  );
}
