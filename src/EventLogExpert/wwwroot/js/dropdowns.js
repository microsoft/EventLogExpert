window.toggleDropdown = (root, isVisible) => {
    const dropdown = root.getElementsByClassName("dropdown-list");

    if (isVisible) {
        const bounds = root.getBoundingClientRect();

        dropdown[0].style.position = "fixed";
        dropdown[0].style.top = `${bounds.bottom + 4}px`;
        dropdown[0].style.left = `${bounds.left}px`;
        dropdown[0].style.width = `${bounds.width}px`;
    } else {
        dropdown[0].style.position = false;
        dropdown[0].style.top = false;
        dropdown[0].style.left = false;
        dropdown[0].style.width = false;
    }
}
