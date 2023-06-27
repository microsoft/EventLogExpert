window.toggleDropdown = (root, isVisible) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    if (isVisible) {
        const bounds = root.getBoundingClientRect();

        dropdown.style.position = "fixed";
        dropdown.style.top = `${bounds.bottom + 4}px`;
        dropdown.style.left = `${bounds.left}px`;
        dropdown.style.width = `${bounds.width}px`;
    } else {
        dropdown.style.position = false;
        dropdown.style.top = false;
        dropdown.style.left = false;
        dropdown.style.width = false;
    }
}

window.scrollToItem = (elementId) => {
    const element = document.getElementById(elementId);

    if (element) {
        const parent = element.parentElement;

        if (parent) {
            parent.scrollTop = element.offsetTop;
        }
    }
}
