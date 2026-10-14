/**
 * Full-page navigation, e.g. to start a login at the BFF (leaves the SPA).
 * An object rather than a bare function so tests can replace it: jsdom does not let them observe window.location.
 */
export const browser = {
  navigate(url: string): void {
    window.location.href = url;
  },
};