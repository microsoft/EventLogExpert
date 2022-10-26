export function startColumnResize(dotNetObj) {
    document.onmousemove = e => {
        // For some reason we have to manually pass this as a new object. Simply passing the event
        // to .NET does not work. We're only including the one value we care about.
        dotNetObj.invokeMethodAsync('MouseMoveCallback', { ClientX: e.clientX });
    };

    document.onmouseup = e => {
        document.onmousemove = null;
        document.onmouseup = null;
        dotNetObj.invokeMethodAsync('MouseUpCallback');
    };
}
