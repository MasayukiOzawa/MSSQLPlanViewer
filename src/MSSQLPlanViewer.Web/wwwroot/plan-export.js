(function () {
    "use strict";

    function collectSameOriginStyles() {
        const cssChunks = [];
        const sheets = document.styleSheets;

        for (let i = 0; i < sheets.length; i++) {
            const sheet = sheets[i];
            let rules;

            // Accessing cssRules throws for cross-origin stylesheets; skip those.
            try {
                rules = sheet.cssRules;
            } catch {
                continue;
            }

            if (!rules) {
                continue;
            }

            for (let j = 0; j < rules.length; j++) {
                cssChunks.push(rules[j].cssText);
            }
        }

        return cssChunks.join("\n");
    }

    function buildSvgString(svgElement) {
        if (!svgElement) {
            throw new Error("No SVG element was provided.");
        }

        const clone = svgElement.cloneNode(true);
        clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
        clone.setAttribute("xmlns:xlink", "http://www.w3.org/1999/xlink");

        const viewBox = svgElement.getAttribute("viewBox");
        let width = svgElement.clientWidth;
        let height = svgElement.clientHeight;

        if (viewBox) {
            const parts = viewBox.split(/\s+/).map(Number);
            if (parts.length === 4 && parts[2] > 0 && parts[3] > 0) {
                width = parts[2];
                height = parts[3];
            }
        }

        clone.setAttribute("width", width);
        clone.setAttribute("height", height);

        const css = collectSameOriginStyles();
        if (css) {
            const styleElement = document.createElementNS("http://www.w3.org/2000/svg", "style");
            styleElement.textContent = css;
            clone.insertBefore(styleElement, clone.firstChild);
        }

        const serialized = new XMLSerializer().serializeToString(clone);
        return {
            text: "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + serialized,
            width: width,
            height: height
        };
    }

    function triggerBlobDownload(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);

        // Defer revocation so the download can start.
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    window.mssqlPlanViewerExport = {
        exportSvg: function (svgElement, fileName) {
            const result = buildSvgString(svgElement);
            const blob = new Blob([result.text], { type: "image/svg+xml;charset=utf-8" });
            triggerBlobDownload(blob, fileName);
        },

        exportPng: function (svgElement, fileName) {
            return new Promise((resolve) => {
                let result;
                try {
                    result = buildSvgString(svgElement);
                } catch {
                    resolve(false);
                    return;
                }

                const svgBlob = new Blob([result.text], { type: "image/svg+xml;charset=utf-8" });
                const svgUrl = URL.createObjectURL(svgBlob);
                const image = new Image();

                const scale = window.devicePixelRatio && window.devicePixelRatio > 1 ? window.devicePixelRatio : 2;

                image.onload = function () {
                    try {
                        const canvas = document.createElement("canvas");
                        canvas.width = Math.max(1, Math.round(result.width * scale));
                        canvas.height = Math.max(1, Math.round(result.height * scale));

                        const context = canvas.getContext("2d");
                        context.fillStyle = "#ffffff";
                        context.fillRect(0, 0, canvas.width, canvas.height);
                        context.drawImage(image, 0, 0, canvas.width, canvas.height);

                        canvas.toBlob(function (blob) {
                            URL.revokeObjectURL(svgUrl);
                            if (!blob) {
                                resolve(false);
                                return;
                            }
                            triggerBlobDownload(blob, fileName);
                            resolve(true);
                        }, "image/png");
                    } catch {
                        URL.revokeObjectURL(svgUrl);
                        resolve(false);
                    }
                };

                image.onerror = function () {
                    URL.revokeObjectURL(svgUrl);
                    resolve(false);
                };

                image.src = svgUrl;
            });
        },

        downloadText: function (fileName, mimeType, text) {
            const blob = new Blob([text], { type: (mimeType || "text/plain") + ";charset=utf-8" });
            triggerBlobDownload(blob, fileName);
        },

        copyText: async function (text) {
            try {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    await navigator.clipboard.writeText(text);
                    return true;
                }
            } catch {
                // Fall through to the legacy approach below.
            }

            try {
                const textArea = document.createElement("textarea");
                textArea.value = text;
                textArea.style.position = "fixed";
                textArea.style.opacity = "0";
                document.body.appendChild(textArea);
                textArea.focus();
                textArea.select();
                const succeeded = document.execCommand("copy");
                document.body.removeChild(textArea);
                return succeeded;
            } catch {
                return false;
            }
        }
    };
})();
