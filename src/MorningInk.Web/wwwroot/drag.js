// Firefox starts dragging an element only when the drag carries data; Blazor's drag events cannot set any.
document.addEventListener("dragstart", event => event.dataTransfer?.setData("text/plain", ""));
