window.enableDetailsPaneResizer = () => {
    const detailsPane = document.getElementById("details-pane");
    const resizer = document.getElementById("details-resizer");

    if (detailsPane == null || resizer == null) { return; }

    let y, h = 0;

    const mouseMoveHandler = function (e) {
        const distance = e.clientY - y;

        detailsPane.style.height = `${h - distance}px`;
    };

    const mouseUpHandler = function () {
        document.removeEventListener("mousemove", mouseMoveHandler);
        document.removeEventListener("mouseup", mouseUpHandler);
    };

    const mouseDownHandler = function (e) {
        y = e.clientY;

        const styles = window.getComputedStyle(detailsPane);
        h = parseInt(styles.height, 10);

        document.addEventListener("mousemove", mouseMoveHandler);
        document.addEventListener("mouseup", mouseUpHandler);
    };

    resizer.addEventListener("mousedown", mouseDownHandler);
};
