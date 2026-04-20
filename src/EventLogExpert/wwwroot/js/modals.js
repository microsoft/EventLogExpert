window.openModal = (ref) => {
    if (ref != null && !ref.open) {
        ref.showModal();
    }
};

window.closeModal = (ref) => {
    if (ref != null) {
        ref.close();
    }
};
