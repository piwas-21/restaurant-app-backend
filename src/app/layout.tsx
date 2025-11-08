import { Inter } from "next/font/google";
import "./globals.css";
import AppInternalLayout from "./app-internal-layout";
import ClientProviders from "./client-providers";
import { Metadata, Viewport } from 'next';

export const metadata: Metadata = {
  title: 'RUMI Restaurant',
  description: 'RUMI Restaurant - Experience authentic flavors.',
  icons: {
    icon: '/rumi-letter-r-icon.svg',
  },
};

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1.0,
  maximumScale: 5.0,
  userScalable: true,
};

const inter = Inter({ subsets: ["latin"] });

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning={true}>
      <body className={inter.className}>
        <ClientProviders>
          <AppInternalLayout>{children}</AppInternalLayout>
        </ClientProviders>
      </body>
    </html>
  );
}
