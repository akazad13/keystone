import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => cleanup());

// The first render in a test file loads the router and page modules (and recharts); on a busy machine
// or a small CI runner that can take several seconds, far beyond the 1 second default for findBy*/waitFor.
configure({ asyncUtilTimeout: 10_000 });

// jsdom does not implement scrolling; the router's scroll restoration calls it.
window.scrollTo = () => {};

// jsdom has no ResizeObserver; recharts' ResponsiveContainer needs one.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};