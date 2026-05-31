window.mssqlPlanViewerDropzone = {
    init(dropzone) {
        if (!dropzone || dropzone.dataset.dropReady === "true") {
            return;
        }

        const input = dropzone.querySelector('input[type="file"]');
        if (!input) {
            return;
        }

        dropzone.dataset.dropReady = "true";

        const activate = () => dropzone.classList.add("is-dragover");
        const deactivate = () => dropzone.classList.remove("is-dragover");

        const stop = (event) => {
            event.preventDefault();
            event.stopPropagation();
        };

        ["dragenter", "dragover"].forEach((name) => {
            dropzone.addEventListener(name, (event) => {
                stop(event);
                if (event.dataTransfer) {
                    event.dataTransfer.dropEffect = "copy";
                }
                activate();
            });
        });

        ["dragleave", "dragend"].forEach((name) => {
            dropzone.addEventListener(name, (event) => {
                stop(event);
                if (name === "dragleave" && dropzone.contains(event.relatedTarget)) {
                    return;
                }
                deactivate();
            });
        });

        dropzone.addEventListener("drop", (event) => {
            stop(event);
            deactivate();

            const files = event.dataTransfer && event.dataTransfer.files;
            if (!files || files.length === 0) {
                return;
            }

            input.files = files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
        });
    }
};
