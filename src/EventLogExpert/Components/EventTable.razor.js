﻿export function enableColumnResize(table) {
    const columns = table.querySelectorAll('th');

    if (columns != null) {

        const calculateTableHeight = function () {
            let height = 0;
            const tableRows = table.querySelectorAll('tr');

            tableRows.forEach(x => {
                let firstColumn = x.querySelector('th:first-child,td:first-child');
                if (firstColumn != null) {
                    height += firstColumn.offsetHeight;
                }
            });

            return height;
        }

        let actualHeight = calculateTableHeight();

        const createResizableColumn = function (column) {
            let x = 0;
            let w = 0;

            const divider = document.createElement('div');
            divider.classList.add('table-divider');

            divider.style.height = `${actualHeight}px`;

            column.appendChild(divider);

            const mouseDownHandler = function (e) {
                x = e.clientX;

                const styles = window.getComputedStyle(column);
                w = parseInt(styles.width, 10);

                document.addEventListener('mousemove', mouseMoveHandler);
                document.addEventListener('mouseup', mouseUpHandler);
            };

            const mouseMoveHandler = function (e) {
                const distance = e.clientX - x;

                column.style.width = `${w + distance}px`;
            };

            const mouseUpHandler = function () {
                document.removeEventListener('mousemove', mouseMoveHandler);
                document.removeEventListener('mouseup', mouseUpHandler);
            };

            divider.addEventListener('mousedown', mouseDownHandler);
        };

        [].forEach.call(columns, function (column) {
            createResizableColumn(column);
        });
    }
}
