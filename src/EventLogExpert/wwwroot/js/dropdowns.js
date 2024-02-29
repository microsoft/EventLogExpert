window.registerDropdown = (root) => {
    const input = root.getElementsByTagName("input")[0];

    if (input) {
        input.addEventListener("blur",
            (e) => {
                const target = e.currentTarget.parentNode;

                requestAnimationFrame(() => {
                    if (target.contains(document.activeElement)) { return; }

                    closeDropdown(root);
                });
            });
    }

    root.addEventListener("blur",
        (e) => {
            const target = e.currentTarget;

            requestAnimationFrame(() => {
                if (target.contains(document.activeElement)) { return; }

                closeDropdown(root);
            });
        });

    root.addEventListener("click", (e) => toggleDropdown(root));
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
