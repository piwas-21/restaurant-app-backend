import React from 'react';
import { useTranslation } from 'react-i18next';
import { NotificationSoundType } from '@/hooks/useNotification';
import styles from './SoundSelector.module.css';

interface SoundSelectorProps {
  isOpen: boolean;
  onClose: () => void;
  soundType: NotificationSoundType;
  onSoundTypeChange: (type: NotificationSoundType) => void;
  onTestSound: (type: NotificationSoundType) => void;
  repeatUntilMouseMoves: boolean;
  onToggleRepeat: () => void;
}

const SOUND_OPTIONS = [
  { value: 'chime' as const, label: 'Chime', desc: 'Pleasant & Medium' },
  { value: 'bell' as const, label: 'Bell', desc: 'Loud & Long' },
  { value: 'ping' as const, label: 'Ping', desc: 'Soft & Short' },
  { value: 'alert' as const, label: 'Alert', desc: 'Loud & Short' },
  { value: 'melody' as const, label: 'Melody', desc: 'Soft & Long' }
];

export default function SoundSelector({
  isOpen,
  onClose,
  soundType,
  onSoundTypeChange,
  onTestSound,
  repeatUntilMouseMoves,
  onToggleRepeat
}: SoundSelectorProps) {
  const { t } = useTranslation();

  if (!isOpen) return null;

  return (
    <div className={styles.dropdown}>
      <div className={styles.header}>
        {t('select_notification_sound') || 'Select Notification Sound'}
      </div>

      {SOUND_OPTIONS.map((sound) => (
        <div
          key={sound.value}
          className={`${styles.soundOption} ${soundType === sound.value ? styles.soundOptionActive : ''}`}
          onClick={() => {
            onSoundTypeChange(sound.value);
            onClose();
          }}
        >
          <div>
            <div className={styles.soundLabel}>
              {sound.label}
            </div>
            <div className={styles.soundDesc}>
              {sound.desc}
            </div>
          </div>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onTestSound(sound.value);
            }}
            className={styles.testButton}
          >
            🔊 Test
          </button>
        </div>
      ))}

      {/* Repeat Sound Toggle */}
      <div className={styles.repeatSection}>
        <div
          onClick={onToggleRepeat}
          className={`${styles.repeatToggle} ${repeatUntilMouseMoves ? styles.repeatToggleActive : ''}`}
        >
          <div className={styles.repeatContent}>
            <div className={styles.repeatTitle}>
              <span className={styles.repeatIcon}>🔁</span>
              <span>
                {t('repeat_until_mouse_moves') || 'Repeat until mouse moves'}
              </span>
            </div>
            <div className={styles.repeatDesc}>
              {t('repeat_sound_desc') || 'Keeps playing until you return to the desk'}
            </div>
          </div>

          {/* Toggle Switch */}
          <div className={`${styles.toggleSwitch} ${repeatUntilMouseMoves ? styles.toggleSwitchActive : ''}`}>
            <div className={styles.toggleKnob} />
          </div>
        </div>
      </div>

      <div className={styles.footer}>
        💡 {t('click_to_select_or_test') || 'Click a sound to select, or test before choosing'}
      </div>
    </div>
  );
}
