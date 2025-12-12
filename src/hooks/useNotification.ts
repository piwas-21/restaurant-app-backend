'use client';

import { useState, useCallback, useRef } from 'react';

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
  const [audioEnabled, setAudioEnabled] = useState(false);

  // Initialize AudioContext on first user interaction
  const initializeAudio = useCallback(() => {
    if (audioContextRef.current) return;

    try {
      const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
      audioContextRef.current = new AudioContextClass();
      setAudioEnabled(true);
      console.log('Audio initialized successfully');
    } catch (error) {
      console.error('Could not initialize audio context:', error);
    }
  }, []);

  // Toggle audio on/off
  const toggleAudio = useCallback(() => {
    if (!audioContextRef.current) {
      initializeAudio();
    } else {
      setAudioEnabled(prev => !prev);
      console.log(`Audio ${!audioEnabled ? 'enabled' : 'disabled'}`);
    }
  }, [initializeAudio, audioEnabled]);

  // Play notification sound using Web Audio API
  const playNotificationSound = useCallback(() => {
    if (!audioContextRef.current || !audioEnabled) {
      if (!audioContextRef.current) {
        console.warn('AudioContext not initialized. User interaction required.');
      }
      return;
    }

    try {
      const audioContext = audioContextRef.current;
      
      // Resume context if suspended (required by some browsers)
      if (audioContext.state === 'suspended') {
        audioContext.resume();
      }

      const now = audioContext.currentTime;

      // Pleasant notification chime (3 notes: E5, G#5, E6)
      const playNote = (frequency: number, startTime: number, duration: number, volume: number) => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();
        
        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);
        
        // Use sine wave for pleasant tone
        oscillator.type = 'sine';
        oscillator.frequency.setValueAtTime(frequency, startTime);
        
        // Envelope: quick attack, slow decay
        gainNode.gain.setValueAtTime(0, startTime);
        gainNode.gain.linearRampToValueAtTime(volume, startTime + 0.01);
        gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + duration);
        
        oscillator.start(startTime);
        oscillator.stop(startTime + duration);
      };

      // Musical chime: E5 (659.25 Hz), G#5 (830.61 Hz), E6 (1318.51 Hz)
      playNote(659.25, now, 0.3, 0.2);           // E5
      playNote(830.61, now + 0.1, 0.4, 0.15);    // G#5
      playNote(1318.51, now + 0.2, 0.6, 0.1);    // E6 (high note)

    } catch (error) {
      console.error('Could not play notification sound:', error);
    }
  }, [audioEnabled]);

  const addNotification = useCallback(
    (notification: Omit<Notification, 'id'>) => {
      const id = `notification-${++notificationIdRef.current}`;
      const newNotification: Notification = {
        ...notification,
        id,
        duration: notification.duration ?? 5000, // Default 5 seconds
      };

      // Play sound if requested
      if (newNotification.sound) {
        playNotificationSound();
      }

      setNotifications((prev) => [newNotification, ...prev]);

      // Auto-dismiss if duration is set
      if (newNotification.duration && newNotification.duration > 0) {
        const timeoutId = setTimeout(() => {
          removeNotification(id);
        }, newNotification.duration);

        return { id, timeoutId };
      }

      return { id };
    },
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
    },
    [addNotification]
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

  return {
    notifications,
    addNotification,
    removeNotification,
    notifyNewOrder,
    notifyOrderReady,
    audioEnabled,
    toggleAudio,
  };
}
