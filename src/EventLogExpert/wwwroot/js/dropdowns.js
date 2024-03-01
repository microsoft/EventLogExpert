window.registerDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];

    const closeDropdown = (e) => {
        const target = e.currentTarget.parentNode;

        requestAnimationFrame(() => {
            if (target.contains(document.activeElement)) { return; }

            dropdown.removeAttribute("data-toggle");
        });
    };

    const scrollToSelectedItem = () => {
        const items = dropdown.getElementsByTagName("div");

        for (let i = 0; i < items.length; i++) {
            if (items[i].getAttribute("selected") !== null) {
                items[i].scrollIntoView({ block: "nearest" });

                return;
            }
        }
    };

    const toggleDropdown = () => {
        const bounds = root.getBoundingClientRect();

        dropdown.style.position = "fixed";
        dropdown.style.top = `${bounds.bottom + 4}px`;
        dropdown.style.left = `${bounds.left}px`;
        dropdown.style.width = `${bounds.width}px`;

        if (dropdown.toggleAttribute("data-toggle")) {
            scrollToSelectedItem();
        }
    };

    input.addEventListener("click", (e) => toggleDropdown(root));
    input.addEventListener("blur", (e) => closeDropdown(e));

    dropdown.addEventListener("blur", (e) => closeDropdown(e));
};

window.closeDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    dropdown.removeAttribute("data-toggle");
};

window.scrollToSelectedItem = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const items = dropdown.getElementsByTagName("div");

    for (let i = 0; i < items.length; i++) {
        if (items[i].getAttribute("selected") !== null) {
            items[i].scrollIntoView({ block: "nearest" });

            return;
        }
    }
};

window.toggleDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const bounds = root.getBoundingClientRect();

    dropdown.style.position = "fixed";
    dropdown.style.top = `${bounds.bottom + 4}px`;
    dropdown.style.left = `${bounds.left}px`;
    dropdown.style.width = `${bounds.width}px`;

    if (dropdown.toggleAttribute("data-toggle")) {
        scrollToSelectedItem(root);
    }
};
