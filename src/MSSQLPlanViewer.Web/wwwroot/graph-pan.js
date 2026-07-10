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

    scrollTableRowIntoView(element, nodeId) {
        if (!element || !nodeId) {
            return;
        }

        const target = element.querySelector(`[data-table-node-id="${CSS.escape(nodeId)}"]`);
        if (!target) {
            return;
        }

        const shellRect = element.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const targetTop = targetRect.top - shellRect.top + element.scrollTop;
        const centeredTop = targetTop - (element.clientHeight - target.offsetHeight) / 2;
        const maxTop = Math.max(0, element.scrollHeight - element.clientHeight);
        const nextTop = Math.min(Math.max(0, centeredTop), maxTop);

        element.scrollTo({
            top: nextTop,
            left: element.scrollLeft,
            behavior: "smooth"
        });
    },

    focusTableRow(element, nodeId) {
        this.scrollTableRowIntoView(element, nodeId);
    },

    initPanelResizer(panel, handle, options) {
        if (!panel || !handle || handle.dataset.resizeReady === "true") {
            return;
        }

        handle.dataset.resizeReady = "true";

        const minHeightPx = Number.isFinite(options?.minHeightPx) ? options.minHeightPx : 240;
        const minWidthPx = Number.isFinite(options?.minWidthPx) ? options.minWidthPx : 320;
        let activePointerId = null;
        let capturedPointerId = null;
        let startX = 0;
        let startY = 0;
        let startWidth = 0;
        let startHeight = 0;
        const resizeTrackElement = typeof options?.resizeTrackSelector === "string"
            ? panel.closest(options.resizeTrackSelector)
            : null;
        const resizeTrackProperty = typeof options?.resizeTrackProperty === "string"
            ? options.resizeTrackProperty
            : "--resized-panel-width";
        const contentResizeSelector = typeof options?.contentResizeSelector === "string"
            ? options.contentResizeSelector
            : null;
        const minContentHeightPx = Number.isFinite(options?.minContentHeightPx) ? options.minContentHeightPx : 288;

        const getPixelValue = value => Number.parseFloat(value) || 0;

        const getOuterHeight = element => {
            const style = getComputedStyle(element);
            return element.getBoundingClientRect().height + getPixelValue(style.marginTop) + getPixelValue(style.marginBottom);
        };

        const setResizeTrackWidth = widthPx => {
            if (!resizeTrackElement) {
                return;
            }

            resizeTrackElement.style.setProperty(resizeTrackProperty, `${widthPx.toFixed(0)}px`);
        };

        const syncContentHeight = () => {
            if (!contentResizeSelector) {
                return;
            }

            const content = panel.querySelector(contentResizeSelector);
            if (!content) {
                return;
            }

            let occupiedHeight = 0;
            for (const child of Array.from(panel.children)) {
                const childStyle = getComputedStyle(child);
                if (child === content || child.contains(content) || childStyle.position === "absolute") {
                    continue;
                }

                occupiedHeight += getOuterHeight(child);
            }

            const panelStyle = getComputedStyle(panel);
            const verticalPadding = getPixelValue(panelStyle.paddingTop) + getPixelValue(panelStyle.paddingBottom);
            const nextContentHeight = Math.max(minContentHeightPx, panel.clientHeight - verticalPadding - occupiedHeight);
            content.style.height = `${nextContentHeight.toFixed(0)}px`;
            content.style.minHeight = `${minContentHeightPx.toFixed(0)}px`;
        };

        const getMaxWidth = () => {
            if (Number.isFinite(options?.maxWidthPx)) {
                return Math.max(minWidthPx, options.maxWidthPx);
            }

            if (options?.maxWidthMode === "viewport") {
                const panelLeft = panel.getBoundingClientRect().left;
                const rightMarginPx = Number.isFinite(options?.rightMarginPx) ? options.rightMarginPx : 24;
                let maxRight = window.innerWidth - rightMarginPx;

                if (typeof options?.maxRightSelector === "string" && options.maxRightSelector.length > 0) {
                    const blockingPanels = Array.from(document.querySelectorAll(options.maxRightSelector));
                    for (const blockingPanel of blockingPanels) {
                        const blockingRect = blockingPanel.getBoundingClientRect();
                        if (blockingRect.width > 0 && blockingRect.left > panelLeft) {
                            maxRight = Math.min(maxRight, blockingRect.left - rightMarginPx);
                        }
                    }
                }

                return Math.max(minWidthPx, maxRight - panelLeft);
            }

            const parentWidth = panel.parentElement?.clientWidth ?? window.innerWidth;
            return Math.max(minWidthPx, parentWidth);
        };

        const setSize = (widthPx, heightPx) => {
            const nextWidth = Math.min(getMaxWidth(), Math.max(minWidthPx, widthPx));
            panel.dataset.panelResized = "true";
            panel.style.width = `${nextWidth.toFixed(0)}px`;
            panel.style.height = `${Math.max(minHeightPx, heightPx).toFixed(0)}px`;
            setResizeTrackWidth(nextWidth);
            syncContentHeight();
        };

        const beginResizing = (clientX, clientY, pointerId) => {
            activePointerId = pointerId;
            startX = clientX;
            startY = clientY;
            startWidth = panel.getBoundingClientRect().width;
            startHeight = panel.getBoundingClientRect().height;
            panel.style.width = `${startWidth.toFixed(0)}px`;
            panel.style.height = `${startHeight.toFixed(0)}px`;
            setResizeTrackWidth(startWidth);
            syncContentHeight();
            document.body.classList.add("plan-panel-resizing");
        };

        const updateResizing = (clientX, clientY) => {
            if (activePointerId === null) {
                return;
            }

            setSize(startWidth + clientX - startX, startHeight + clientY - startY);
        };

        const stopResizing = () => {
            if (activePointerId === null) {
                return;
            }

            if (capturedPointerId !== null && handle.hasPointerCapture(capturedPointerId)) {
                handle.releasePointerCapture(capturedPointerId);
            }

            activePointerId = null;
            capturedPointerId = null;
            document.body.classList.remove("plan-panel-resizing");
        };

        handle.addEventListener("pointerdown", event => {
            if (event.button !== 0) {
                return;
            }

            event.preventDefault();
            beginResizing(event.clientX, event.clientY, event.pointerId);
            capturedPointerId = event.pointerId;
            handle.setPointerCapture(event.pointerId);
        });

        const handlePointerMove = event => {
            if (event.pointerId !== activePointerId) {
                return;
            }

            event.preventDefault();
            updateResizing(event.clientX, event.clientY);
        };

        const handlePointerEnd = event => {
            if (event.pointerId !== activePointerId) {
                return;
            }

            stopResizing();
        };

        handle.addEventListener("pointermove", handlePointerMove);
        window.addEventListener("pointermove", handlePointerMove);
        handle.addEventListener("pointerup", handlePointerEnd);
        window.addEventListener("pointerup", handlePointerEnd);
        handle.addEventListener("pointercancel", handlePointerEnd);
        window.addEventListener("pointercancel", handlePointerEnd);

        handle.addEventListener("mousedown", event => {
            if (event.button !== 0 || activePointerId !== null) {
                return;
            }

            event.preventDefault();
            beginResizing(event.clientX, event.clientY, "mouse");
        });

        window.addEventListener("mousemove", event => {
            if (activePointerId !== "mouse") {
                return;
            }

            event.preventDefault();
            updateResizing(event.clientX, event.clientY);
        });

        window.addEventListener("mouseup", () => {
            if (activePointerId === "mouse") {
                stopResizing();
            }
        });
    },

    resetPanelResizer(panel, options) {
        if (!panel) {
            return;
        }

        const resizeTrackElement = typeof options?.resizeTrackSelector === "string"
            ? panel.closest(options.resizeTrackSelector)
            : null;
        const resizeTrackProperty = typeof options?.resizeTrackProperty === "string"
            ? options.resizeTrackProperty
            : "--resized-panel-width";
        const contentResizeSelector = typeof options?.contentResizeSelector === "string"
            ? options.contentResizeSelector
            : null;

        delete panel.dataset.panelResized;
        panel.style.removeProperty("width");
        panel.style.removeProperty("height");

        if (resizeTrackElement) {
            resizeTrackElement.style.removeProperty(resizeTrackProperty);
        }

        if (contentResizeSelector) {
            const content = panel.querySelector(contentResizeSelector);
            if (content) {
                content.style.removeProperty("height");
                content.style.removeProperty("min-height");
            }
        }

        document.body.classList.remove("plan-panel-resizing");
    },

    initVerticalResizer(panel, handle, options) {
        this.initPanelResizer(panel, handle, options);
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
