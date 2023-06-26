window.registerTableColumnResizers = () => {
    const tables = document.querySelectorAll("table");

    tables.forEach(table => deleteColumnResize(table));
    tables.forEach(table => enableColumnResize(table));
};

window.deleteColumnResize = (table) => {
    table.querySelectorAll(".table-divider").forEach(x => x.remove());
};

window.enableColumnResize = (table) => {
    const columns = table.querySelectorAll("th");

    if (columns != null) {
        const createResizableColumn = function (column) {
            let x = 0;
            let w = 0;

            const divider = document.createElement("div");
            divider.classList.add("table-divider");

            column.appendChild(divider);

            const mouseMoveHandler = function (e) {
                const distance = e.clientX - x;

                column.style.width = `${w + distance}px`;
            };

            const mouseUpHandler = function () {
                document.removeEventListener("mousemove", mouseMoveHandler);
                document.removeEventListener("mouseup", mouseUpHandler);

                window.deleteColumnResize(table);
                window.enableColumnResize(table);
            };

            const mouseDownHandler = function (e) {
                x = e.clientX;

                const styles = window.getComputedStyle(column);
                w = parseInt(styles.width, 10);

                document.addEventListener("mousemove", mouseMoveHandler);
                document.addEventListener("mouseup", mouseUpHandler);
            };

            divider.addEventListener("mousedown", mouseDownHandler);
        };

        for (let i = 0; i < columns.length - 1; i++) {
            createResizableColumn(columns[i]);
        }
    }
};
