'use client';

import { useState, useCallback, useRef, useEffect } from 'react';

export type NotificationSoundType =
  | 'chime' // Default: Pleasant 3-note chime (medium)
  | 'bell' // Loud & Long: Classic bell sound
  | 'ping' // Soft & Short: Gentle single ping
  | 'alert' // Loud & Short: Urgent alert
  | 'melody'; // Soft & Long: Calming melody

export interface Notification {
  id: string;
  type: 'success' | 'error' | 'info' | 'warning';
  title: string;
  message: string;
  duration?: number; // in milliseconds, 0 = no auto-dismiss
  sound?: boolean; // play sound notification
}

export function useNotification() {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const notificationIdRef = useRef(0);
  const audioContextRef = useRef<AudioContext | null>(null);
  const [audioEnabled, setAudioEnabled] = useState(true);
  const [audioReady, setAudioReady] = useState(false);
  const [audioBlockedByPolicy, setAudioBlockedByPolicy] = useState(false);
  const [soundType, setSoundType] = useState<NotificationSoundType>('chime');
  const [repeatUntilMouseMoves, setRepeatUntilMouseMoves] = useState(false);
  const repeatIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const hasUserInteractedRef = useRef(false);

  // Initialize AudioContext on first user interaction
  const initializeAudio = useCallback(() => {
    if (audioContextRef.current) {
      console.log('🔊 AudioContext already exists, state:', audioContextRef.current.state);
      return;
    }

    try {
      const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
      audioContextRef.current = new AudioContextClass();

      const state = audioContextRef.current.state;
      console.log('🔊 AudioContext created, state:', state);

      if (state === 'running') {
        setAudioReady(true);
        setAudioBlockedByPolicy(false);
        console.log('✅ Audio is ready to play');
      } else if (state === 'suspended') {
        setAudioBlockedByPolicy(true);
        console.warn('⚠️ AudioContext is suspended - user interaction required');
      }

      setAudioEnabled(true);
    } catch (error) {
      console.error('❌ Could not initialize audio context:', error);
      setAudioBlockedByPolicy(true);
    }
  }, []);

  // Resume audio context (requires user gesture on Firefox)
  const resumeAudioContext = useCallback(async () => {
    if (!audioContextRef.current) {
      console.log('🔊 No AudioContext to resume, initializing...');
      initializeAudio();
      return;
    }

    try {
      if (audioContextRef.current.state === 'suspended') {
        console.log('🔊 Attempting to resume suspended AudioContext...');
        await audioContextRef.current.resume();
        console.log('✅ AudioContext resumed, state:', audioContextRef.current.state);
        setAudioReady(true);
        setAudioBlockedByPolicy(false);
        hasUserInteractedRef.current = true;
      } else {
        console.log('🔊 AudioContext state:', audioContextRef.current.state);
        setAudioReady(audioContextRef.current.state === 'running');
      }
    } catch (error) {
      console.error('❌ Failed to resume AudioContext:', error);
      setAudioBlockedByPolicy(true);
    }
  }, [initializeAudio]);

  // Load sound type preference from localStorage
  useEffect(() => {
    const savedSoundType = localStorage.getItem('cashier_notification_sound');
    if (savedSoundType && ['chime', 'bell', 'ping', 'alert', 'melody'].includes(savedSoundType)) {
      setSoundType(savedSoundType as NotificationSoundType);
    }

    const savedRepeatSetting = localStorage.getItem('cashier_repeat_sound');
    if (savedRepeatSetting === 'true') {
      setRepeatUntilMouseMoves(true);
    }
  }, []);

  // Auto-initialize audio on mount
  useEffect(() => {
    if (audioEnabled && !audioContextRef.current) {
      requestAnimationFrame(() => {
        console.log('🔊 Auto-initializing audio context...');
        initializeAudio();
      });
    }
  }, [audioEnabled, initializeAudio]);

  // Set up event listeners for user interaction to resume audio
  useEffect(() => {
    const handleUserInteraction = () => {
      if (!hasUserInteractedRef.current) {
        console.log('👆 User interaction detected, attempting to enable audio...');
        hasUserInteractedRef.current = true;
        resumeAudioContext();
      }
    };

    const events = ['click', 'touchstart', 'keydown'];
    events.forEach(event => {
      document.addEventListener(event, handleUserInteraction, { once: true });
    });

    return () => {
      events.forEach(event => {
        document.removeEventListener(event, handleUserInteraction);
      });
    };
  }, [resumeAudioContext]);

  // Resume audio context when tab becomes visible (important for Firefox/Chrome)
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && audioContextRef.current) {
        const state = audioContextRef.current.state;
        console.log('👁️ Tab visible, AudioContext state:', state);

        if (state === 'suspended' && hasUserInteractedRef.current) {
          console.log('🔊 Attempting to resume audio after visibility change...');
          audioContextRef.current.resume().then(() => {
            setAudioReady(audioContextRef.current?.state === 'running');
            setAudioBlockedByPolicy(audioContextRef.current?.state !== 'running');
          }).catch(err => {
            console.warn('⚠️ Failed to resume audio on visibility change:', err);
          });
        } else if (state === 'running') {
          setAudioReady(true);
          setAudioBlockedByPolicy(false);
        }
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, []);

  // Toggle audio on/off
  const toggleAudio = useCallback(async () => {
    if (!audioContextRef.current) {
      initializeAudio();
      setTimeout(() => resumeAudioContext(), 100);
    } else {
      const newEnabled = !audioEnabled;
      setAudioEnabled(newEnabled);
      console.log(`🔊 Audio ${newEnabled ? 'enabled' : 'disabled'}`);

      if (newEnabled && audioContextRef.current.state === 'suspended') {
        await resumeAudioContext();
      }
    }
  }, [initializeAudio, resumeAudioContext, audioEnabled]);

  // Helper function to play notes
  const playNotes = useCallback((audioContext: AudioContext, type: NotificationSoundType) => {
    const now = audioContext.currentTime;

    const playNote = (frequency: number, startTime: number, duration: number, volume: number, waveType: OscillatorType = 'sine') => {
      const oscillator = audioContext.createOscillator();
      const gainNode = audioContext.createGain();

      oscillator.connect(gainNode);
      gainNode.connect(audioContext.destination);

      oscillator.type = waveType;
      oscillator.frequency.setValueAtTime(frequency, startTime);

      gainNode.gain.setValueAtTime(0, startTime);
      gainNode.gain.linearRampToValueAtTime(volume, startTime + 0.01);
      gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + duration);

      oscillator.start(startTime);
      oscillator.stop(startTime + duration);
    };

    switch (type) {
      case 'chime':
        playNote(659.25, now, 0.3, 0.2);
        playNote(830.61, now + 0.1, 0.4, 0.15);
        playNote(1318.51, now + 0.2, 0.6, 0.1);
        break;
      case 'bell':
        playNote(1046.5, now, 0.8, 0.3);
        playNote(1318.51, now + 0.05, 0.85, 0.25);
        playNote(1568, now + 0.1, 0.9, 0.2);
        playNote(2093, now + 0.15, 1.0, 0.15);
        break;
      case 'ping':
        playNote(880, now, 0.15, 0.12);
        playNote(1760, now + 0.05, 0.2, 0.08);
        break;
      case 'alert':
        playNote(987.77, now, 0.15, 0.35, 'square');
        playNote(987.77, now + 0.2, 0.15, 0.35, 'square');
        playNote(987.77, now + 0.4, 0.15, 0.35, 'square');
        break;
      case 'melody':
        playNote(523.25, now, 0.4, 0.12);
        playNote(659.25, now + 0.3, 0.4, 0.1);
        playNote(783.99, now + 0.6, 0.4, 0.08);
        playNote(1046.5, now + 0.9, 0.6, 0.1);
        playNote(783.99, now + 1.3, 0.5, 0.08);
        break;
    }
  }, []);

  // Play notification sound
  const playNotificationSound = useCallback(() => {
    if (!audioEnabled) {
      return;
    }

    if (!audioContextRef.current) {
      console.warn('⚠️ AudioContext not initialized');
      setAudioBlockedByPolicy(true);
      initializeAudio();
      return;
    }

    const audioContext = audioContextRef.current;

    // Resume context if suspended
    if (audioContext.state === 'suspended') {
      console.warn('⚠️ AudioContext suspended, attempting resume...');
      audioContext.resume().then(() => {
        if (audioContext.state === 'running') {
          setAudioReady(true);
          setAudioBlockedByPolicy(false);
          playNotes(audioContext, soundType);
        }
      }).catch(err => {
        console.error('Failed to resume AudioContext:', err);
        setAudioBlockedByPolicy(true);
      });
      return;
    }

    try {
      playNotes(audioContext, soundType);
    } catch (error) {
      console.error('Could not play notification sound:', error);
    }
  }, [audioEnabled, soundType, initializeAudio, playNotes]);

  // Play a specific sound type
  const playSoundByType = useCallback((type: NotificationSoundType) => {
    if (!audioContextRef.current || !audioEnabled) {
      return;
    }

    try {
      const audioContext = audioContextRef.current;

      if (audioContext.state === 'suspended') {
        audioContext.resume();
      }

      playNotes(audioContext, type);
    } catch (error) {
      console.error('Could not play notification sound:', error);
    }
  }, [audioEnabled, playNotes]);

  // Stop repeating sound
  const stopRepeating = useCallback(() => {
    if (repeatIntervalRef.current) {
      clearInterval(repeatIntervalRef.current);
      repeatIntervalRef.current = null;
    }
  }, []);

  // Start repeating sound until mouse moves
  const startRepeatingUntilMouseMoves = useCallback(() => {
    if (!repeatUntilMouseMoves || !audioEnabled) return;

    stopRepeating();
    playNotificationSound();

    const getRepeatInterval = () => {
      switch (soundType) {
        case 'bell': return 1500;
        case 'melody': return 2200;
        case 'ping': return 600;
        case 'alert': return 1000;
        case 'chime':
        default: return 1200;
      }
    };

    repeatIntervalRef.current = setInterval(() => {
      playNotificationSound();
    }, getRepeatInterval());

    const handleMouseMove = () => {
      stopRepeating();
      document.removeEventListener('mousemove', handleMouseMove);
    };

    document.addEventListener('mousemove', handleMouseMove, { once: true });
  }, [repeatUntilMouseMoves, audioEnabled, playNotificationSound, stopRepeating, soundType]);

  // Clean up repeat interval on unmount
  useEffect(() => {
    return () => {
      stopRepeating();
    };
  }, [stopRepeating]);

  const addNotification = useCallback(
    (notification: Omit<Notification, 'id'>) => {
      const id = `notification-${++notificationIdRef.current}`;
      const newNotification: Notification = {
        ...notification,
        id,
        duration: notification.duration ?? 5000,
      };

      if (newNotification.sound) {
        playNotificationSound();
      }

      setNotifications((prev) => [newNotification, ...prev]);

      if (newNotification.duration && newNotification.duration > 0) {
        const timeoutId = setTimeout(() => {
          removeNotification(id);
        }, newNotification.duration);

        return { id, timeoutId };
      }

      return { id };
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [playNotificationSound]
  );

  const removeNotification = useCallback((id: string) => {
    setNotifications((prev) => prev.filter((n) => n.id !== id));
  }, []);

  const notifyNewOrder = useCallback(
    (orderNumber: string, customerName: string) => {
      addNotification({
        type: 'info',
        title: '🎉 New Order Received!',
        message: `Order #${orderNumber}${customerName ? ` from ${customerName}` : ''}`,
        duration: 8000,
        sound: true,
      });

      if (repeatUntilMouseMoves) {
        startRepeatingUntilMouseMoves();
      }
    },
    [addNotification, repeatUntilMouseMoves, startRepeatingUntilMouseMoves]
  );

  const notifyOrderReady = useCallback(
    (orderNumber: string) => {
      addNotification({
        type: 'success',
        title: '✅ Order Ready!',
        message: `Order #${orderNumber} is ready for pickup`,
        duration: 6000,
        sound: true,
      });
    },
    [addNotification]
  );

  const notifyOrderUpdate = useCallback(
    (orderNumber: string, status: string) => {
      addNotification({
        type: 'info',
        title: '🔔 Order Updated!',
        message: `Order #${orderNumber} status changed to ${status}`,
        duration: 6000,
        sound: true,
      });
    },
    [addNotification]
  );

  // Play a different notification sound for order updates
  const playOrderUpdateSound = useCallback(() => {
    if (!audioContextRef.current || !audioEnabled) return;

    try {
      const audioContext = audioContextRef.current;

      if (audioContext.state === 'suspended') {
        audioContext.resume();
      }

      const now = audioContext.currentTime;

      const playNote = (frequency: number, startTime: number, duration: number, volume: number) => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);

        oscillator.type = 'sine';
        oscillator.frequency.setValueAtTime(frequency, startTime);

        gainNode.gain.setValueAtTime(0, startTime);
        gainNode.gain.linearRampToValueAtTime(volume, startTime + 0.01);
        gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + duration);

        oscillator.start(startTime);
        oscillator.stop(startTime + duration);
      };

      // Lower, softer sound
      playNote(261.63, now, 0.25, 0.15);
      playNote(329.63, now + 0.08, 0.3, 0.12);
      playNote(392, now + 0.16, 0.5, 0.08);

    } catch (error) {
      console.error('Could not play order update sound:', error);
    }
  }, [audioEnabled]);

  // Change notification sound type
  const changeSoundType = useCallback((newType: NotificationSoundType) => {
    setSoundType(newType);
    localStorage.setItem('cashier_notification_sound', newType);
    console.log('🔊 Notification sound changed to:', newType);
  }, []);

  // Toggle repeat until mouse moves
  const toggleRepeatSound = useCallback(() => {
    const newValue = !repeatUntilMouseMoves;
    setRepeatUntilMouseMoves(newValue);
    localStorage.setItem('cashier_repeat_sound', newValue.toString());
    console.log('🔁 Repeat sound until mouse moves:', newValue);
  }, [repeatUntilMouseMoves]);

  return {
    notifications,
    addNotification,
    removeNotification,
    notifyNewOrder,
    notifyOrderReady,
    notifyOrderUpdate,
    playOrderUpdateSound,
    audioEnabled,
    audioReady,
    audioBlockedByPolicy,
    toggleAudio,
    resumeAudioContext,
    soundType,
    changeSoundType,
    playNotificationSound,
    playSoundByType,
    repeatUntilMouseMoves,
    toggleRepeatSound,
  };
}
