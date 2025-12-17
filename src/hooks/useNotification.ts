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
  const [soundType, setSoundType] = useState<NotificationSoundType>('chime');
  const [repeatUntilMouseMoves, setRepeatUntilMouseMoves] = useState(false);
  const repeatIntervalRef = useRef<NodeJS.Timeout | null>(null);

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

  // Auto-initialize audio on mount if enabled by default
  useEffect(() => {
    if (audioEnabled && !audioContextRef.current) {
      // Use requestAnimationFrame to avoid blocking other initializations (like SSE)
      requestAnimationFrame(() => {
        console.log('🔊 Auto-initializing audio context...');
        initializeAudio();
      });
    }
  }, [audioEnabled, initializeAudio]);

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

      // Helper function to play a note
      const playNote = (frequency: number, startTime: number, duration: number, volume: number, waveType: OscillatorType = 'sine') => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();
        
        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);
        
        oscillator.type = waveType;
        oscillator.frequency.setValueAtTime(frequency, startTime);
        
        // Envelope: quick attack, slow decay
        gainNode.gain.setValueAtTime(0, startTime);
        gainNode.gain.linearRampToValueAtTime(volume, startTime + 0.01);
        gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + duration);
        
        oscillator.start(startTime);
        oscillator.stop(startTime + duration);
      };

      // Play different sounds based on selected type
      switch (soundType) {
        case 'chime': // Default: Pleasant 3-note chime (medium)
          playNote(659.25, now, 0.3, 0.2);           // E5
          playNote(830.61, now + 0.1, 0.4, 0.15);    // G#5
          playNote(1318.51, now + 0.2, 0.6, 0.1);    // E6
          break;

        case 'bell': // Loud & Long: Classic bell sound
          playNote(1046.5, now, 0.8, 0.3);           // C6
          playNote(1318.51, now + 0.05, 0.85, 0.25); // E6
          playNote(1568, now + 0.1, 0.9, 0.2);       // G6
          playNote(2093, now + 0.15, 1.0, 0.15);     // C7
          break;

        case 'ping': // Soft & Short: Gentle single ping
          playNote(880, now, 0.15, 0.12);            // A5
          playNote(1760, now + 0.05, 0.2, 0.08);     // A6
          break;

        case 'alert': // Loud & Short: Urgent alert
          playNote(987.77, now, 0.15, 0.35, 'square');     // B5
          playNote(987.77, now + 0.2, 0.15, 0.35, 'square'); // B5
          playNote(987.77, now + 0.4, 0.15, 0.35, 'square'); // B5
          break;

        case 'melody': // Soft & Long: Calming melody
          playNote(523.25, now, 0.4, 0.12);          // C5
          playNote(659.25, now + 0.3, 0.4, 0.1);     // E5
          playNote(783.99, now + 0.6, 0.4, 0.08);    // G5
          playNote(1046.5, now + 0.9, 0.6, 0.1);     // C6
          playNote(783.99, now + 1.3, 0.5, 0.08);    // G5
          break;
      }

    } catch (error) {
      console.error('Could not play notification sound:', error);
    }
  }, [audioEnabled, soundType]);

  // Play a specific sound type (useful for testing)
  const playSoundByType = useCallback((type: NotificationSoundType) => {
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

      // Helper function to play a note
      const playNote = (frequency: number, startTime: number, duration: number, volume: number, waveType: OscillatorType = 'sine') => {
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();
        
        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);
        
        oscillator.type = waveType;
        oscillator.frequency.setValueAtTime(frequency, startTime);
        
        // Envelope: quick attack, slow decay
        gainNode.gain.setValueAtTime(0, startTime);
        gainNode.gain.linearRampToValueAtTime(volume, startTime + 0.01);
        gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + duration);
        
        oscillator.start(startTime);
        oscillator.stop(startTime + duration);
      };

      // Play different sounds based on provided type
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

    } catch (error) {
      console.error('Could not play notification sound:', error);
    }
  }, [audioEnabled]);

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

    // Stop any existing repeat
    stopRepeating();

    // Play immediately
    playNotificationSound();

    // Calculate repeat interval based on sound type duration
    const getRepeatInterval = () => {
      switch (soundType) {
        case 'bell': return 1500; // Longer sound
        case 'melody': return 2200; // Longest sound
        case 'ping': return 600; // Shortest sound
        case 'alert': return 1000; // Short bursts
        case 'chime': 
        default: return 1200;
      }
    };

    // Set up repeating
    repeatIntervalRef.current = setInterval(() => {
      playNotificationSound();
    }, getRepeatInterval());

    // Set up mouse move listener to stop
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
      
      // Use repeating sound if enabled
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

  // Play a different notification sound for order updates (lower pitch, softer)
  const playOrderUpdateSound = useCallback(() => {
    if (!audioContextRef.current || !audioEnabled) return;

    try {
      const audioContext = audioContextRef.current;
      
      if (audioContext.state === 'suspended') {
        audioContext.resume();
      }

      const now = audioContext.currentTime;

      // Different sound: C4, E4, G4 (softer, lower pitch than new order)
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

      // Lower, softer sound: C4 (261.63 Hz), E4 (329.63 Hz), G4 (392 Hz)
      playNote(261.63, now, 0.25, 0.15);         // C4
      playNote(329.63, now + 0.08, 0.3, 0.12);   // E4
      playNote(392, now + 0.16, 0.5, 0.08);      // G4

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
    toggleAudio,
    soundType,
    changeSoundType,
    playNotificationSound, // Expose for testing
    playSoundByType, // Expose for testing specific sounds
    repeatUntilMouseMoves,
    toggleRepeatSound,
  };
}
