window.invokeContextMenu = (x, y) => {
    const contextMenu = document.getElementById("context-menu");

    contextMenu.style.position = "fixed";
    contextMenu.style.top = `${y}px`;
    contextMenu.style.left = `${x}px`;

    contextMenu.classList.add("active");
};

window.addEventListener("click", function(e) {
    const contextMenu = document.getElementById("context-menu");

    contextMenu.style.position = false;
    contextMenu.style.top = false;
    contextMenu.style.left = false;

    contextMenu.classList.remove("active")
});