import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Enable standalone output for Docker containers
  output: 'standalone',

  experimental: {
    // allowedDevOrigins can be added if needed for cloud workstations
  },

  /* Security Configuration */
  async headers() {
    return [
      {
        // Apply security headers to all routes
        source: '/:path*',
        headers: [
          {
            key: 'X-DNS-Prefetch-Control',
            value: 'on'
          },
          {
            key: 'Strict-Transport-Security',
            value: 'max-age=63072000; includeSubDomains; preload'
          },
          {
            key: 'X-Frame-Options',
            value: 'SAMEORIGIN'
          },
          {
            key: 'X-Content-Type-Options',
            value: 'nosniff'
          },
          {
            key: 'X-XSS-Protection',
            value: '1; mode=block'
          },
          {
            key: 'Referrer-Policy',
            value: 'strict-origin-when-cross-origin'
          },
          {
            key: 'Permissions-Policy',
            value: 'camera=(), microphone=(), geolocation=()'
          },
          {
            key: 'Content-Security-Policy',
            value: [
              "default-src 'self'",
              "script-src 'self' 'unsafe-eval' 'unsafe-inline' https://accounts.google.com https://www.gstatic.com",
              "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
              "font-src 'self' https://fonts.gstatic.com data:",
              "img-src 'self' data: https: blob:",
              "connect-src 'self' http://localhost:5221 https://www.rumirestaurant.ch https://accounts.google.com https://www.gstatic.com",
              "frame-src 'self' https://accounts.google.com https://www.google.com https://maps.google.com",
              "object-src 'none'",
              "base-uri 'self'",
              "form-action 'self'",
              "frame-ancestors 'self'",
              "upgrade-insecure-requests"
            ].join('; ')
          }
        ]
      }
    ];
  },

  // Disable powered-by header
  poweredByHeader: false,

  // Production optimizations
  compress: true,

  // Image optimization security
  images: {
    remotePatterns: [
      {
        protocol: 'https',
        hostname: '**.rumirestaurant.ch',
      },
      {
        protocol: 'https',
        hostname: 'restaurant-admin-api.orderhub.ch',
        pathname: '/web-images/**',
      },
      {
        protocol: 'https',
        hostname: 'lh3.google.com',
        pathname: '/u/0/d/**',
      },
      {
        protocol: 'https',
        hostname: 'rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com',
        pathname: '/**',
      }
    ],
    deviceSizes: [640, 828, 1200],
    imageSizes: [64, 96, 128],
    formats: ['image/webp'],
    unoptimized: true,
    dangerouslyAllowSVG: false,
    contentDispositionType: 'attachment',
    contentSecurityPolicy: "default-src 'self'; script-src 'none'; sandbox;",
  }
};

export default nextConfig;
