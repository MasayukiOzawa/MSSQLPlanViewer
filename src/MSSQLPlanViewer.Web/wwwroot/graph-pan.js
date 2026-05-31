window.mssqlPlanViewerGraphPan = {
    init(element) {
        if (!element || element.dataset.panReady === "true") {
            return;
        }

        element.dataset.panReady = "true";

        let isPointerDown = false;
        let isDragging = false;
        let startX = 0;
        let startY = 0;
        let startScrollLeft = 0;
        let startScrollTop = 0;

        const stopDragging = () => {
            if (!isPointerDown && !isDragging) {
                return;
            }

            isPointerDown = false;
            element.classList.remove("is-dragging");
            window.setTimeout(() => {
                isDragging = false;
            }, 0);
        };

        element.addEventListener("mousedown", event => {
            if (event.button !== 0) {
                return;
            }

            isPointerDown = true;
            isDragging = false;
            startX = event.clientX;
            startY = event.clientY;
            startScrollLeft = element.scrollLeft;
            startScrollTop = element.scrollTop;
        });

        window.addEventListener("mousemove", event => {
            if (!isPointerDown) {
                return;
            }

            const deltaX = event.clientX - startX;
            const deltaY = event.clientY - startY;

            if (!isDragging && Math.abs(deltaX) < 3 && Math.abs(deltaY) < 3) {
                return;
            }

            isDragging = true;
            element.classList.add("is-dragging");
            element.scrollLeft = startScrollLeft - deltaX;
            element.scrollTop = startScrollTop - deltaY;
        });

        window.addEventListener("mouseup", stopDragging);
        element.addEventListener("mouseleave", stopDragging);

        element.addEventListener("click", event => {
            if (!isDragging) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
        }, true);
    },

    reset(element) {
        if (!element) {
            return;
        }

        element.scrollLeft = 0;
        element.scrollTop = 0;
        element.classList.remove("is-dragging");
    },

    focusNode(element, nodeId) {
        if (!element || !nodeId) {
            return;
        }

        const target = element.querySelector(`[data-node-id="${CSS.escape(nodeId)}"]`);
        if (!target) {
            return;
        }

        const targetRect = target.getBoundingClientRect();
        const shellRect = element.getBoundingClientRect();
        const offsetLeft = targetRect.left - shellRect.left;
        const offsetTop = targetRect.top - shellRect.top;
        const nextScrollLeft = element.scrollLeft + offsetLeft - ((shellRect.width - targetRect.width) / 2);
        const nextScrollTop = element.scrollTop + offsetTop - ((shellRect.height - targetRect.height) / 2);

        element.scrollLeft = Math.max(0, nextScrollLeft);
        element.scrollTop = Math.max(0, nextScrollTop);
    },

    focusGlobalNode(nodeId) {
        if (!nodeId) {
            return;
        }

        const shell = document.querySelector(".graph-shell");
        if (!shell) {
            return;
        }

        shell.querySelectorAll(".graph-node.selected-via-table").forEach(node => {
            node.classList.remove("selected-via-table");
        });

        const target = shell.querySelector(`[data-node-id="${CSS.escape(nodeId)}"]`);
        if (!target) {
            return;
        }

        target.classList.add("selected-via-table");
        this.focusNode(shell, nodeId);
    },

    focusTableRow(element, nodeId) {
        if (!element || !nodeId) {
            return;
        }

        const target = element.querySelector(`[data-table-node-id="${CSS.escape(nodeId)}"]`);
        if (!target) {
            return;
        }

        target.scrollIntoView({
            block: "center",
            inline: "nearest",
            behavior: "smooth"
        });
    },

    initDetailsResizer(panel, handle) {
        if (!panel || !handle || handle.dataset.resizeReady === "true") {
            return;
        }

        handle.dataset.resizeReady = "true";

        const root = document.documentElement;
        const minWidthRem = 24;
        const maxWidthRem = 48;
        let activePointerId = null;
        let panelRight = 0;

        const getRootFontSize = () => {
            const rootFontSize = Number.parseFloat(getComputedStyle(root).fontSize);
            return Number.isFinite(rootFontSize) && rootFontSize > 0 ? rootFontSize : 16;
        };

        const clamp = value => {
            const rootFontSize = getRootFontSize();
            const minWidthPx = minWidthRem * rootFontSize;
            const maxWidthPx = maxWidthRem * rootFontSize;
            return Math.min(maxWidthPx, Math.max(minWidthPx, value));
        };

        const setWidth = widthPx => {
            const nextWidthPx = clamp(widthPx);
            const nextWidthRem = nextWidthPx / getRootFontSize();
            root.style.setProperty("--operator-details-width", `${nextWidthRem.toFixed(2)}rem`);
        };

        const stopResizing = pointerId => {
            if (activePointerId !== pointerId) {
                return;
            }

            if (handle.hasPointerCapture(pointerId)) {
                handle.releasePointerCapture(pointerId);
            }

            activePointerId = null;
            document.body.classList.remove("details-resizing");
        };

        handle.addEventListener("pointerdown", event => {
            if (event.button !== 0) {
                return;
            }

            event.preventDefault();
            activePointerId = event.pointerId;
            panelRight = panel.getBoundingClientRect().right;
            handle.setPointerCapture(event.pointerId);
            document.body.classList.add("details-resizing");
        });

        handle.addEventListener("pointermove", event => {
            if (event.pointerId !== activePointerId) {
                return;
            }

            event.preventDefault();
            setWidth(panelRight - event.clientX);
        });

        handle.addEventListener("pointerup", event => stopResizing(event.pointerId));
        handle.addEventListener("pointercancel", event => stopResizing(event.pointerId));
    }
};
