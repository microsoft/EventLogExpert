window.openSettingsModal = () => {
    const settingsModal = document.getElementById("settingsDialog");

    if (settingsModal != null) {
        settingsModal.showModal();
    }
};

window.closeSettingsModal = () => {
    const settingsModal = document.getElementById("settingsDialog");

    if (settingsModal != null) {
        settingsModal.close();
    }
};

window.openFilterCacheModal = () => {
    const filterCacheModal = document.getElementById("filterCacheDialog");

    if (filterCacheModal != null) {
        filterCacheModal.showModal();
    }
};

window.closeFilterCacheModal = () => {
    const filterCacheModal = document.getElementById("filterCacheDialog");

    if (filterCacheModal != null) {
        filterCacheModal.close();
    }
};