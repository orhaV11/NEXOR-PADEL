// Shared helpers for every Venuvoy ad page: format from the URL hash, the club crest and icons.
(function () {
  const format = (location.hash || "#feed").slice(1);
  document.documentElement.dataset.format = format;

  /* The crest from src/components/layout/Monogram.tsx: a serif V inside a double brass ring. */
  const crest = (band = "#16392b", brass = "#b8955a", fg = "#f3ecdc") => `
    <svg viewBox="0 0 40 40" aria-hidden="true">
      <circle cx="20" cy="20" r="19" fill="${band}"/>
      <circle cx="20" cy="20" r="18.25" fill="none" stroke="${brass}" stroke-width="1"/>
      <circle cx="20" cy="20" r="15.6" fill="none" stroke="${brass}" stroke-width="0.6"/>
      <path d="M20 1.2 21 2.9 20 4.6 19 2.9Z" fill="${brass}"/>
      <path d="M20 35.4 21 37.1 20 38.8 19 37.1Z" fill="${brass}"/>
      <path d="M11.2 11.6h6.6v.75l-1.6.2 4.35 11.4 3.95-10.6c.25-.68.05-.95-.65-1.05l-1.05-.15v-.75h5.05v.75l-.75.12c-.6.1-.92.4-1.2 1.12L20.3 28.9h-.95l-6.3-16.1c-.25-.62-.55-.9-1.1-.98l-.75-.1Z" fill="${fg}"/>
    </svg>`;

  const icons = {
    // Forward in RTL points left.
    arrow: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5"/><path d="M11 6l-6 6 6 6"/></svg>`,
    check: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>`,
    link: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1"/><path d="M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1"/></svg>`,
    train: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="5" y="3" width="14" height="13" rx="3"/><path d="M5 10h14"/><path d="M9 20l-2 2M15 20l2 2M8 16l-1 4M16 16l1 4"/><circle cx="9" cy="13" r=".6" fill="currentColor"/><circle cx="15" cy="13" r=".6" fill="currentColor"/></svg>`,
    ball: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7.2l3.6 2.6-1.4 4.2H9.8L8.4 9.8z" fill="currentColor"/><path d="M12 7.2V3.3M15.6 9.8l3.7-1.2M14.2 14l2.3 3.2M9.8 14l-2.3 3.2M8.4 9.8L4.7 8.6"/></svg>`,
    bell: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/></svg>`,
    swap: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3l4 4-4 4"/><path d="M3 7h18"/><path d="M7 21l-4-4 4-4"/><path d="M21 17H3"/></svg>`,
    pin: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M12 22s7-6.3 7-12a7 7 0 1 0-14 0c0 5.7 7 12 7 12z"/><circle cx="12" cy="10" r="2.6"/></svg>`,
    clock: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>`,
  };

  function hydrate(root = document) {
    root.querySelectorAll(".crest:not([data-done])").forEach((el) => {
      const v = el.dataset.variant || "default";
      el.innerHTML =
        v === "gilt"
          ? crest("#0d2219", "#cdae72", "#e6cf98")
          : v === "ox"
            ? crest("#3d0d16", "#cdae72", "#f3ecdc")
            : crest();
      el.dataset.done = "1";
    });
    root.querySelectorAll("[data-icon]:not([data-done])").forEach((el) => {
      el.innerHTML = icons[el.dataset.icon] || "";
      el.dataset.done = "1";
    });
  }

  window.VV = { crest, icons, hydrate, format };
  document.addEventListener("DOMContentLoaded", () => hydrate());
  window.__ready = (async () => {
    await new Promise((r) =>
      document.readyState === "loading" ? document.addEventListener("DOMContentLoaded", r) : r(),
    );
    hydrate();
    await document.fonts.ready;
    // Make sure every face used on the page is actually loaded before a screenshot.
    const probes = [
      '800 40px "FRL"',
      '400 40px "FRL"',
      '700 40px "Assistant"',
      '400 40px "Assistant"',
      '500 40px "Playfair"',
      'italic 500 40px "Playfair"',
    ];
    await Promise.all(probes.map((p) => document.fonts.load(p, "אבג Abc 0123 ₪")));
    await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
    return true;
  })();
})();
