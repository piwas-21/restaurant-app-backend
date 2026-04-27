import { dirname } from "path";
import { fileURLToPath } from "url";
import { FlatCompat } from "@eslint/eslintrc";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const compat = new FlatCompat({
  baseDirectory: __dirname,
});

const eslintConfig = [
  ...compat.extends("next/core-web-vitals", "next/typescript"),
  {
    rules: {
      // Disable TypeScript-specific rules that are too strict
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unsafe-assignment": "off",
      "@typescript-eslint/no-unsafe-member-access": "off",
      "@typescript-eslint/no-unsafe-return": "off",
      "@typescript-eslint/no-unsafe-call": "off",
      "@typescript-eslint/no-unused-vars": [
        "warn",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
          destructuredArrayIgnorePattern: "^_",
        },
      ],
      "@typescript-eslint/no-unused-expressions": "off",

      // Next.js specific rules
      "@next/next/no-img-element": "warn",
      // The rule's LCP-bandwidth concern doesn't apply to admin-only views,
      // and proper migration to <Image> needs images.remotePatterns config
      // for CMS-backed URLs — tracked separately. See follow-up issue.

      // Other helpful rules to disable during development
      "no-unused-vars": "off", // Turn off base rule as it can conflict with @typescript-eslint/no-unused-vars
      "no-unused-expressions": "off",
      // Permit console.warn/error (legitimate production signals) but flag
      // console.log as noise. console.debug/info follow the same rationale
      // as .log — they're typically dev-time scaffolding and shouldn't ship.
      "no-console": ["warn", { allow: ["warn", "error"] }],
    },
  },
  {
    // Admin surfaces are internal tooling, not customer-facing LCP
    // critical paths. Disable no-img-element here until the proper
    // next/image migration lands (CMS images + remotePatterns config).
    files: ["src/components/admin/**/*.{ts,tsx}", "src/app/admin/**/*.{ts,tsx}"],
    rules: {
      "@next/next/no-img-element": "off",
    },
  },
];

export default eslintConfig;
