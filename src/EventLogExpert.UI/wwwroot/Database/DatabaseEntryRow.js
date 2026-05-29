// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

export function attachCheckboxKeyHandler(checkboxEl) {
    if (!checkboxEl) {
        return;
    }

    checkboxEl.addEventListener('keydown', (e) => {
        if (e.key === ' ' || e.key === 'Spacebar') {
            e.preventDefault();
        }
    });
}
