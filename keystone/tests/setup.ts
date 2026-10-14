import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => cleanup());

// The first render in a test file loads the router and page modules; under parallel runs that can
// take longer than the 1 second default for findBy*/waitFor.
configure({ asyncUtilTimeout: 5000 });

// jsdom does not implement scrolling; the router's scroll restoration calls it.
window.scrollTo = () => {};

// jsdom has no ResizeObserver; recharts' ResponsiveContainer needs one.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};