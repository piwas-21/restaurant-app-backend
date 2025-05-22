import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import LanguageSwitcher from './LanguageSwitcher';
// Import the hook we want to mock to type it and reset its implementation
import { useCookieConsent } from './CookieConsentContext';

let mockI18nState: { language: string; resolvedLanguage: string; changeLanguage: (lng: string) => Promise<void>; };
const mockUseTranslation = jest.fn();

// Mock react-i18next
jest.mock('react-i18next', () => ({
  useTranslation: () => mockUseTranslation(),
}));

// Mock next/image
jest.mock('next/image', () => ({
  __esModule: true,
  default: ({ src, alt, width, height }: { src: string; alt: string; width?: number; height?: number }) => {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={src} alt={alt} width={width} height={height} />;
  },
}));

// Mock the ENTIRE CookieConsentContext module, specifically its exports
jest.mock('./CookieConsentContext', () => ({
  __esModule: true, // Important if the original module is an ES module
  useCookieConsent: jest.fn(), // This mock function will be used by the component
}));

describe('LanguageSwitcher Component', () => {
  beforeEach(() => {
    // Reset and configure react-i18next mock
    mockI18nState = {
      language: 'en',
      resolvedLanguage: 'en',
      changeLanguage: jest.fn(async (lng) => { // Made async to match Promise<void>
        mockI18nState.language = lng;
        mockI18nState.resolvedLanguage = lng;
      }),
    };
    mockUseTranslation.mockImplementation(() => ({
      i18n: mockI18nState,
      t: jest.fn((key) => key),
    }));

    // Configure the mocked useCookieConsent for each test
    // This ensures that `useCookieConsent` (which is now our jest.fn()) returns the desired object
    (useCookieConsent as jest.Mock).mockReturnValue({
      consent: { preferences: true }, // Default to consent given for preferences
      isConsentPending: false,
      isSettingsModalOpen: false,
      acceptPreferences: jest.fn(),
      declinePreferences: jest.fn(),
      updateConsent: jest.fn(),
      openSettingsModal: jest.fn(),
      closeSettingsModal: jest.fn(),
    });

    localStorage.clear();
  });

  // Optional: Clear mock calls between tests if needed, though mockReturnValue in beforeEach usually handles state.
  // afterEach(() => {
  //   jest.clearAllMocks(); 
  // });

  test('renders correctly with initial language (EN)', () => {
    render(<LanguageSwitcher />);
    expect(screen.getByRole('button', { name: /Toggle language menu/i })).toBeInTheDocument();
    expect(screen.getByAltText('English')).toHaveAttribute('src', '/flags/en.svg');
    expect(screen.getByText('EN')).toBeInTheDocument();
  });

  test('dropdown opens and closes on click', async () => {
    render(<LanguageSwitcher />);
    const dropdownButton = screen.getByRole('button', { name: /Toggle language menu/i });

    expect(screen.queryByRole('button', { name: /Deutsch Deutsch/i })).not.toBeInTheDocument();

    fireEvent.click(dropdownButton);
    await waitFor(() => expect(screen.getByRole('button', { name: /Deutsch Deutsch/i })).toBeVisible());
    expect(screen.getByRole('button', { name: /Türkçe Türkçe/i })).toBeVisible();

    fireEvent.click(dropdownButton);
    await waitFor(() => expect(screen.queryByRole('button', { name: /Deutsch Deutsch/i })).not.toBeInTheDocument());
  });

  test('changes language on selecting a language from dropdown and updates button', async () => {
    render(<LanguageSwitcher />);
    const initialButton = screen.getByRole('button', { name: /Toggle language menu/i });
    fireEvent.click(initialButton);

    await waitFor(() => expect(screen.getByRole('button', { name: /Deutsch Deutsch/i })).toBeVisible());
    const germanOptionButton = screen.getByRole('button', { name: /Deutsch Deutsch/i });
    fireEvent.click(germanOptionButton);

    await waitFor(() => {
      expect(mockI18nState.changeLanguage).toHaveBeenCalledWith('de');
    });
    expect(localStorage.getItem('i18nextLng')).toBe('de'); // Because consent.preferences is true by default in mock

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Toggle language menu/i })).toBeInTheDocument();
      expect(screen.getByText('DE')).toBeInTheDocument();
      expect(screen.getByAltText('Deutsch')).toHaveAttribute('src', '/flags/de.svg');
    });
    expect(screen.queryByRole('button', { name: /Deutsch Deutsch/i })).not.toBeInTheDocument();
  });

  test('displays the correct flag and name for a pre-set language (TR)', () => {
    mockI18nState.language = 'tr';
    mockI18nState.resolvedLanguage = 'tr';
    // The mockUseTranslation will pick up the updated mockI18nState due to beforeEach setup

    render(<LanguageSwitcher />);
    
    expect(screen.getByRole('button', { name: /Toggle language menu/i })).toBeInTheDocument();
    expect(screen.getByAltText('Türkçe')).toHaveAttribute('src', '/flags/tr.svg');
    expect(screen.getByText('TR')).toBeInTheDocument();
  });

  test('closes dropdown when clicking outside', async () => {
    render(
      <div>
        <LanguageSwitcher />
        <div data-testid="outside-element">Outside</div>
      </div>
    );
    const dropdownButton = screen.getByRole('button', { name: /Toggle language menu/i });

    fireEvent.click(dropdownButton);
    await waitFor(() => expect(screen.getByRole('button', { name: /Deutsch Deutsch/i })).toBeVisible());

    fireEvent.mouseDown(screen.getByTestId('outside-element'));
    await waitFor(() => expect(screen.queryByRole('button', { name: /Deutsch Deutsch/i })).not.toBeInTheDocument());
  });

  test('does NOT save language to localStorage if consent for preferences is not given', async () => {
    // Override the default mock for this specific test
    (useCookieConsent as jest.Mock).mockReturnValue({
      consent: { preferences: false }, // Simulate NO consent for preferences
      isConsentPending: false,
      isSettingsModalOpen: false,
      acceptPreferences: jest.fn(),
      declinePreferences: jest.fn(),
      updateConsent: jest.fn(),
      openSettingsModal: jest.fn(),
      closeSettingsModal: jest.fn(),
    });

    render(<LanguageSwitcher />);
    const initialButton = screen.getByRole('button', { name: /Toggle language menu/i });
    fireEvent.click(initialButton);

    await waitFor(() => expect(screen.getByRole('button', { name: /Deutsch Deutsch/i })).toBeVisible());
    const germanOptionButton = screen.getByRole('button', { name: /Deutsch Deutsch/i });
    fireEvent.click(germanOptionButton);

    await waitFor(() => {
      expect(mockI18nState.changeLanguage).toHaveBeenCalledWith('de');
    });
    expect(localStorage.getItem('i18nextLng')).toBeNull();
  });
});
