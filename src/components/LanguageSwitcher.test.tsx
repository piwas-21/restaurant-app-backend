import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import LanguageSwitcher from './LanguageSwitcher';

// Mock react-i18next
const mockChangeLanguage = jest.fn();
const mockUseTranslation = jest.fn(() => ({
  i18n: {
    changeLanguage: mockChangeLanguage,
    language: 'en', // Initial language
  },
}));

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


describe('LanguageSwitcher Component', () => {
  beforeEach(() => {
    // Clear mock calls and localStorage before each test
    mockChangeLanguage.mockClear();
    mockUseTranslation.mockClear();
    localStorage.clear();
  });

  test('renders correctly with initial language', () => {
    render(<LanguageSwitcher />);
    // Check if the button with the current language (English) is displayed
    expect(screen.getByRole('button', { name: /English/i })).toBeInTheDocument();
    expect(screen.getByAltText('English')).toBeInTheDocument();
  });

  test('dropdown opens and closes on click', () => {
    render(<LanguageSwitcher />);
    const dropdownButton = screen.getByRole('button', { name: /English/i });

    // Dropdown should be closed initially
    // Check for a specific language option that should not be visible
    expect(screen.queryByRole('button', { name: /Deutsch/i })).not.toBeVisible();


    // Click to open dropdown
    fireEvent.click(dropdownButton);
    expect(screen.getByRole('button', { name: /Deutsch/i })).toBeVisible();
    expect(screen.getByRole('button', { name: /Türkçe/i })).toBeVisible();

    // Click to close dropdown
    fireEvent.click(dropdownButton);
     // Need to wait for the dropdown to close, items might not be immediately "not.toBeVisible"
    // A better check might be to see if the list itself is gone, or use waitFor
    // For simplicity, we'll check one item. In a real scenario, ensure robust checks.
    expect(screen.queryByRole('button', { name: /Deutsch/i })).not.toBeVisible();
  });

  test('changes language on selecting a language from dropdown', () => {
    render(<LanguageSwitcher />);
    const dropdownButton = screen.getByRole('button', { name: /English/i });

    // Open dropdown
    fireEvent.click(dropdownButton);

    // Select German
    const germanOption = screen.getByRole('button', { name: /Deutsch/i });
    fireEvent.click(germanOption);

    // Check if i18n.changeLanguage was called with 'de'
    expect(mockChangeLanguage).toHaveBeenCalledWith('de');

    // Check if localStorage was updated
    expect(localStorage.getItem('i18nextLng')).toBe('de');

    // Dropdown should be closed after selection
    expect(screen.queryByRole('button', { name: /Deutsch/i })).not.toBeVisible();
  });

  test('displays the correct flag and name for the current language', () => {
    // Reset mock to simulate a different initial language
    mockUseTranslation.mockImplementation(() => ({
      i18n: {
        changeLanguage: mockChangeLanguage,
        language: 'tr', // Set initial language to Turkish
      },
    }));
    render(<LanguageSwitcher />);
    expect(screen.getByRole('button', { name: /Türkçe/i })).toBeInTheDocument();
    expect(screen.getByAltText('Türkçe')).toHaveAttribute('src', '/flags/tr.svg');
  });

    test('closes dropdown when clicking outside', () => {
    render(
      <div>
        <LanguageSwitcher />
        <div data-testid="outside-element">Outside</div>
      </div>
    );
    const dropdownButton = screen.getByRole('button', { name: /English/i });

    // Open dropdown
    fireEvent.click(dropdownButton);
    expect(screen.getByRole('button', { name: /Deutsch/i })).toBeVisible();

    // Click outside the dropdown
    fireEvent.mouseDown(screen.getByTestId('outside-element'));
    expect(screen.queryByRole('button', { name: /Deutsch/i })).not.toBeVisible();
  });
});
