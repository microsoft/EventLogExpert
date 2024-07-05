window.registerDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];

    const closeDropdown = (e, force = false) => {
        const target = e.currentTarget.parentNode;

        requestAnimationFrame(() => {
            if (force === false && target.contains(document.activeElement)) { return; }

            dropdown.removeAttribute("data-toggle");
            dropdown.setAttribute("aria-expanded", "false");

            dropdown.style.position = false;
            dropdown.style.top = false;
            dropdown.style.left = false;
            dropdown.style.width = false;
        });
    };

    const scrollToSelectedItem = () => {
        const items = dropdown.getElementsByTagName("div");

        for (let i = 0; i < items.length; i++) {
            if (items[i].hasAttribute("selected")) {
                items[i].scrollIntoView({ block: "nearest" });

                return;
            }
        }
    };

    const openDropdown = () => {
        const bounds = root.getBoundingClientRect();

        dropdown.style.position = "fixed";
        dropdown.style.top = `${bounds.bottom + 4}px`;
        dropdown.style.left = `${bounds.left}px`;
        dropdown.style.width = `${bounds.width}px`;

        dropdown.setAttribute("data-toggle", "");
        dropdown.setAttribute("aria-expanded", "true");

        scrollToSelectedItem();
    }

    const toggleDropdown = (e) => {
        if (dropdown.hasAttribute("data-toggle")) {
            closeDropdown(e, true);
        } else {
            openDropdown();
        }
    };

    input.addEventListener("mousedown", (e) => {
        e.stopPropagation();

        toggleDropdown(e);
    });
    input.addEventListener("blur", (e) => closeDropdown(e));

    dropdown.addEventListener("blur", (e) => closeDropdown(e));
};

window.closeDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    dropdown.removeAttribute("data-toggle");
    dropdown.setAttribute("aria-expanded", "false");

    dropdown.style.position = false;
    dropdown.style.top = false;
    dropdown.style.left = false;
    dropdown.style.width = false;
};

window.openDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const bounds = root.getBoundingClientRect();

    if (dropdown.hasAttribute("data-toggle")) { return; }

    dropdown.style.position = "fixed";
    dropdown.style.top = `${bounds.bottom + 4}px`;
    dropdown.style.left = `${bounds.left}px`;
    dropdown.style.width = `${bounds.width}px`;

    dropdown.setAttribute("data-toggle", "");
    dropdown.setAttribute("aria-expanded", "true");

    scrollToSelectedItem(root);
};

window.scrollToHighlightedItem = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const items = dropdown.getElementsByTagName("div");

    for (let i = 0; i < items.length; i++) {
        if (items[i].hasAttribute("highlighted")) {
            items[i].scrollIntoView({ block: "nearest" });

            return;
        }
    }
};

window.scrollToSelectedItem = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const items = dropdown.getElementsByTagName("div");

    for (let i = 0; i < items.length; i++) {
        if (items[i].hasAttribute("selected")) {
            items[i].scrollIntoView({ block: "nearest" });

            return;
        }
    }
};

window.toggleDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    if (dropdown.hasAttribute("data-toggle")) {
        closeDropdown(root);
    } else {
        openDropdown(root);
    }
};
