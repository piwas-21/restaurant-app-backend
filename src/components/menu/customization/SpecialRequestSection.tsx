"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import styles from "./SpecialRequestSection.module.css";

interface SpecialRequestSectionProps {
  specialInstructions: string;
  onInstructionsChange: (instructions: string) => void;
}

const MAX_CHARACTERS = 200;

export default function SpecialRequestSection({
  specialInstructions,
  onInstructionsChange,
}: SpecialRequestSectionProps) {
  const { t } = useTranslation();

  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const value = e.target.value;
    if (value.length <= MAX_CHARACTERS) {
      onInstructionsChange(value);
    }
  };

  return (
    <div className={styles.section}>
      <h3 className={styles.sectionTitle}>{t("product_special_requests")}</h3>
      <p className={styles.sectionDescription}>{t("make_it_yours")}</p>

      <textarea
        value={specialInstructions}
        onChange={handleChange}
        placeholder={t("product_special_requests_placeholder")}
        className={styles.textarea}
        rows={3}
        aria-label={t("product_special_requests")}
      />

      <div className={styles.characterCount}>
        {t("character_limit", {
          current: specialInstructions.length,
          max: MAX_CHARACTERS,
        })}
      </div>
    </div>
  );
}
