// Counter is a sibling of the editor, not inside contenteditable, so typing is never swallowed.
window.countedTextArea = {
    attach: function (root, netRef) {
        if (!root) return false;
        if (root.dataset.countedBound === "1") return true;
        var surface = root.querySelector(".counted-textarea__surface");
        if (!surface) return false;
        root.dataset.countedBound = "1";

        var counter = root.querySelector(":scope > .counted-textarea__counter");
        if (!counter) {
            counter = document.createElement("span");
            counter.className = "counted-textarea__counter";
            counter.setAttribute("aria-hidden", "true");
            root.appendChild(counter);
        }

        function maxLen() {
            return parseInt(root.getAttribute("data-max-length") || "0", 10) || 0;
        }

        function read() {
            return (surface.innerText || "").replace(/\u00a0/g, " ").replace(/\r/g, "");
        }

        function write(text) {
            surface.textContent = text || "";
        }

        function paint(text) {
            var max = maxLen();
            counter.textContent = (text || "").length + "/" + max;
            root.classList.toggle("counted-textarea--empty", !(text || "").length);
        }

        function emit() {
            var text = read();
            var max = maxLen();
            if (max > 0 && text.length > max) {
                text = text.slice(0, max);
                write(text);
            }
            paint(text);
            if (netRef)
                netRef.invokeMethodAsync("NotifyInput", text);
        }

        surface.addEventListener("input", emit);
        surface.addEventListener("paste", function (e) {
            e.preventDefault();
            var t = (e.clipboardData || window.clipboardData).getData("text/plain") || "";
            document.execCommand("insertText", false, t);
        });

        root._countedTextArea = {
            setText: function (value) {
                var next = value || "";
                if (read() === next) {
                    paint(next);
                    return;
                }
                if (document.activeElement === surface) return;
                write(next);
                paint(next);
            },
            layout: function () { paint(read()); }
        };

        paint(read());
        return true;
    },

    setText: function (root, value) {
        if (root && root._countedTextArea)
            root._countedTextArea.setText(value);
    },

    layout: function (root) {
        if (root && root._countedTextArea)
            root._countedTextArea.layout();
    }
};
