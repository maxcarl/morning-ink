// The arrows beside a sideways-scrolling row jump about a screen of it; each names its row by aria-controls and its
// direction by data-scroll-row (-1 back, 1 on). Listening on the document keeps them working however Blazor renders
// the row, and an arrow with nowhere to go is disabled, which the style sheet hides.
(() => {
    const step = 0.9; // a little overlap, so the cover at the edge stays in sight

    const arrows = () => document.querySelectorAll("[data-scroll-row]");

    const update = () => arrows().forEach(arrow => {
        const row = document.getElementById(arrow.getAttribute("aria-controls"));
        if (!row) {
            return;
        }

        const end = row.scrollWidth - row.clientWidth - 1; // rounding leaves a fraction of a pixel at the end
        arrow.disabled = Number(arrow.dataset.scrollRow) < 0 ? row.scrollLeft <= 0 : row.scrollLeft >= end;
    });

    let pending = false;
    const later = () => {
        if (!pending) {
            pending = true;
            requestAnimationFrame(() => {
                pending = false;
                update();
            });
        }
    };

    document.addEventListener("click", event => {
        const arrow = event.target.closest("[data-scroll-row]");
        const row = arrow && document.getElementById(arrow.getAttribute("aria-controls"));
        row?.scrollBy({ left: Number(arrow.dataset.scrollRow) * row.clientWidth * step, behavior: "smooth" });
    });

    // Scroll events do not bubble, so they are caught on the way down; new covers arrive as DOM changes.
    document.addEventListener("scroll", later, true);
    window.addEventListener("resize", later);
    new MutationObserver(later).observe(document.documentElement, { childList: true, subtree: true });
})();
