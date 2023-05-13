
window.openSettingsModal = () => {
    const settingsModal = document.getElementById("settingsDialog");

    if (settingsModal != null) {
        settingsModal.showModal();
    }
}

window.closeSettingsModal = () => {
    const settingsModal = document.getElementById("settingsDialog");

    if (settingsModal != null) {
        settingsModal.close();
    }
}