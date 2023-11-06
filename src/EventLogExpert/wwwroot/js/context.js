window.invokeContextMenu = (x, y) => {
    const contextMenu = document.getElementById("context-menu");

    contextMenu.style.position = "fixed";
    contextMenu.style.top = `${y}px`;
    contextMenu.style.left = `${x}px`;

    contextMenu.classList.add("active");
};

window.invokeTableColumnMenu = (x, y) => {
    const tableColumnMenu = document.getElementById("table-column-menu");

    tableColumnMenu.style.position = "fixed";
    tableColumnMenu.style.top = `${y}px`;
    tableColumnMenu.style.left = `${x}px`;

    tableColumnMenu.classList.add("active");
};

window.addEventListener("click", function(e) {
    const contextMenu = document.getElementById("context-menu");

    if (!contextMenu.classList.contains("active")) { return; }

    contextMenu.style.position = false;
    contextMenu.style.top = false;
    contextMenu.style.left = false;

    contextMenu.classList.remove("active");
});

window.addEventListener("click", function(e) {
    const tableColumnMenu = document.getElementById("table-column-menu");

    if (!tableColumnMenu.classList.contains("active")) { return; }

    tableColumnMenu.style.position = false;
    tableColumnMenu.style.top = false;
    tableColumnMenu.style.left = false;

    tableColumnMenu.classList.remove("active");
});
