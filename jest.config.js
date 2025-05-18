
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  setupFilesAfterEnv: ['@testing-library/jest-dom/extend-expect'],
  moduleNameMapper: {
    // Handle CSS imports (if you're using them in your components)
    '\.(css|less|scss|sass)$          ': 'identity-obj-proxy',
    // Handle image imports
    '\.(jpg|jpeg|png|gif|eot|otf|webp|svg|ttf|woff|woff2|mp4|webm|wav|mp3|m4a|aac|oga)$          ': '<rootDir>/__mocks__/fileMock.js',
    // Handle module aliases (if you have them in tsconfig.json)
    '^@/components/(.*)$': '<rootDir>/src/components/$1',
    '^@/app/(.*)$': '<rootDir>/src/app/$1',
    // Mock next/router for components that use it (like Link)
    'next/router': '<rootDir>/__mocks__/nextRouterMock.js',
  },
  transform: {
    // Use babel-jest to transpile tests with the next/babel preset
    // This includes support for jsx, typescript, and dynamic imports
    '^.+\.(js|jsx|ts|tsx)$          ': ['babel-jest', { presets: ['next/babel'] }],
  },
  // Ignore transpiling node_modules except for specific ones if needed
  transformIgnorePatterns: [
    '/node_modules/',
    '^.+\.module\.(css|sass|scss)$',
  ],
  // Add custom reporters if needed
  // reporters: ['default', 'jest-junit'],
  // Coverage configuration
  collectCoverage: true,
  collectCoverageFrom: [
    'src/components/**/*.tsx', // Adjust the path to match your project structure
    'src/app/**/*.tsx',       // Adjust the path to match your project structure
    '!src/**/*.test.tsx',
    '!src/**/*.spec.tsx',
  ],
  coverageReporters: ['json', 'lcov', 'text', 'clover'],
  coverageThreshold: {
    global: {
      branches: 80,
      functions: 80,
      lines: 80,
      statements: -10, // Or your desired threshold, -10 means 10 lines uncovered is okay
    },
  },
};
