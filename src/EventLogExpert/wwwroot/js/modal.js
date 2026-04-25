// Capture the previously focused element on open and restore it on close so closing a modal
// (Esc, native cancel, footer buttons) returns keyboard focus to the trigger that opened it,
// matching native dialog accessibility expectations (WAI-ARIA Authoring Practices: Dialog).
window.openModal = (ref) => {
    if (ref == null || ref.open) { return; }

    // Stash on the dialog element so close can restore even if the host component is torn down
    // before close runs (e.g. async disposal racing with native cancel).
    const previouslyFocused = document.activeElement;
    ref._returnFocusElement = (previouslyFocused instanceof HTMLElement && previouslyFocused !== document.body)
        ? previouslyFocused
        : null;

    ref.showModal();
};

window.showModal = window.openModal;

window.closeModal = (ref) => {
    if (ref == null) { return; }

    const returnTarget = ref._returnFocusElement;
    ref._returnFocusElement = null;

    if (ref.open) { ref.close(); }

    // Defer focus to the next frame so the dialog's close + DOM detach completes first; otherwise
    // browsers may move focus to <body> after we set it.
    if (returnTarget && document.body.contains(returnTarget) && typeof returnTarget.focus === "function") {
        requestAnimationFrame(() => {
            try { returnTarget.focus({ preventScroll: true }); }
            catch { /* element detached between frames; nothing to restore */ }
        });
    }
};
