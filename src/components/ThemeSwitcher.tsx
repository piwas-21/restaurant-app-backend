// src/components/ThemeSwitcher.tsx
"use client";

import React from "react";
import { useTheme } from "./ThemeContext";
import styles from "@/app/styles/ThemeSwitcher.module.css"; // Create this CSS module
import { useTranslation } from "react-i18next";
import { Moon, Sun } from "lucide-react";

const ThemeSwitcher: React.FC = () => {
  const { theme, toggleTheme } = useTheme();
  const { t } = useTranslation();

  return (
    <button
      onClick={toggleTheme}
      className={styles.themeSwitcher}
      aria-label={theme === "light" ? t("switch_to_dark_mode") : t("switch_to_light_mode")}
      title={theme === "light" ? t("switch_to_dark_mode") : t("switch_to_light_mode")}
    >
      {theme === "light" ? <Moon size={20} /> : <Sun size={20} />}
    </button>
  );
};

export default ThemeSwitcher;
