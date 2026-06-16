// SPDX-License-Identifier: MIT
// Freya Online -- SPA entry. No SSR: pure client render.
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
// Global layer only: design tokens, the breakpoint scale, app chrome + shared
// primitives, and the reusable UI components. Each screen pulls in its own
// scoped styles/*.module.css; there is no monolithic global screen stylesheet.
import './styles/tokens.css';
import './styles/breakpoints.css';
import './styles/base.css';
import './styles/app.css';

createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <App />
    </StrictMode>,
);
