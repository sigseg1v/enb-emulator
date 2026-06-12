// SPDX-License-Identifier: MIT
// Freya Online web -- ESLint flat config (ESLint 9 + typescript-eslint).
import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';

export default tseslint.config(
  { ignores: ['dist', 'node_modules'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      // The wire layer leans on a few intentional `any`s at JSON boundaries;
      // warn (don't error) so they're visible without blocking the build.
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },
  // Node-context config files (vite, this file) use Node globals, not browser.
  {
    files: ['*.config.{js,ts}'],
    languageOptions: { globals: globals.node },
  },
);
