window.registerDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];
    const controller = new AbortController();

    const closeDropdown = (e, force = false) => {
        const target = e.currentTarget.parentNode;

        requestAnimationFrame(() => {
            if (force === false && target.contains(document.activeElement)) { return; }

            dropdown.removeAttribute("data-toggle");
            dropdown.setAttribute("aria-hidden", "true");
            input.setAttribute("aria-expanded", "false");

            dropdown.style.position = "";
            dropdown.style.top = "";
            dropdown.style.left = "";
            dropdown.style.width = "";
        });
    };

    const scrollToSelectedItem = () => {
        const item = dropdown.querySelector("[aria-selected='true']");
        item?.scrollIntoView({ block: "nearest" });
    };

    const openDropdown = () => {
        const bounds = root.getBoundingClientRect();

        dropdown.style.position = "fixed";
        dropdown.style.top = `${bounds.bottom + 4}px`;
        dropdown.style.left = `${bounds.left}px`;
        dropdown.style.width = `${bounds.width}px`;

        dropdown.setAttribute("data-toggle", "");
        dropdown.setAttribute("aria-hidden", "false");
        input.setAttribute("aria-expanded", "true");

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
    }, { signal: controller.signal });

    input.addEventListener("blur", (e) => closeDropdown(e), { signal: controller.signal });
    dropdown.addEventListener("blur", (e) => closeDropdown(e), { signal: controller.signal });

    root._dropdownController = controller;
};

window.unregisterDropdown = (root) => {
    root?._dropdownController?.abort();
};

window.closeDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];

    dropdown.removeAttribute("data-toggle");
    dropdown.setAttribute("aria-hidden", "true");
    input.setAttribute("aria-expanded", "false");

    dropdown.style.position = "";
    dropdown.style.top = "";
    dropdown.style.left = "";
    dropdown.style.width = "";
};

window.openDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];
    const bounds = root.getBoundingClientRect();

    if (dropdown.hasAttribute("data-toggle")) { return; }

    dropdown.style.position = "fixed";
    dropdown.style.top = `${bounds.bottom + 4}px`;
    dropdown.style.left = `${bounds.left}px`;
    dropdown.style.width = `${bounds.width}px`;

    dropdown.setAttribute("data-toggle", "");
    dropdown.setAttribute("aria-hidden", "false");
    input.setAttribute("aria-expanded", "true");

    window.scrollToSelectedItem(root);
};

window.scrollToHighlightedItem = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const item = dropdown.querySelector("[highlighted]");
    item?.scrollIntoView({ block: "nearest" });
};

window.scrollToSelectedItem = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const item = dropdown.querySelector("[aria-selected='true']");
    item?.scrollIntoView({ block: "nearest" });
};

window.toggleDropdown = (root) => {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    if (dropdown.hasAttribute("data-toggle")) {
        window.closeDropdown(root);
    } else {
        window.openDropdown(root);
    }
};
