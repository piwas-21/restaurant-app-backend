/** @type {import('next').NextConfig} */
const nextConfig = {
  experimental: {
    // allowedDevOrigins: ["3000-firebase-rumi-restaurant-web-1747075444577.cluster-axf5tvtfjjfekvhwxwkkkzsk2y.cloudworkstations.dev"], // Temporarily comment out to avoid warning, will re-evaluate if needed for deployment
  },
  // Other configurations can go here
  images: {
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'restaurant-admin-api.orderhub.ch',
        pathname: '/web-images/**',
      },
      {
        protocol: 'https',
        hostname: 'lh3.google.com',
        pathname: '/u/0/d/**',
      }
    ],
    deviceSizes: [640, 828, 1200],
    imageSizes: [64, 96, 128],
    formats: ['image/webp'],
    unoptimized: true
  },
};

export default nextConfig;

