window.openModal = (ref) => {
    if (ref != null && !ref.open) {
        ref.showModal();
    }
};

window.showModal = window.openModal;

window.closeModal = (ref) => {
    if (ref != null && ref.open) {
        ref.close();
    }
};
