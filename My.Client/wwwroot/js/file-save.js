(function () {
    var nextId = 1;
    var pending = Object.create(null);

    function blobUrl(base64, mime) {
        var bin = atob(base64);
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++)
            bytes[i] = bin.charCodeAt(i);
        return URL.createObjectURL(new Blob([bytes], { type: mime || 'application/octet-stream' }));
    }

    window.fileSave = {
        download: function (base64, mime, fileName) {
            var url = blobUrl(base64, mime);
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'download';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        },

        beginOpen: function () {
            var win = window.open('', '_blank');
            if (!win)
                return '';
            var id = String(nextId++);
            pending[id] = win;
            try {
                win.document.open();
                win.document.write(
                    '<!doctype html><title>Preparing PDF…</title>' +
                    '<body style="font:14px sans-serif;padding:2rem;color:#444">Preparing PDF…</body>');
                win.document.close();
            } catch (e) { /* ignore */ }
            return id;
        },

        completeOpen: function (id, base64, mime) {
            var url = blobUrl(base64, mime);
            var win = id ? pending[id] : null;
            if (id)
                delete pending[id];
            var opened = false;
            if (win && !win.closed) {
                win.location.replace(url);
                opened = true;
            } else {
                opened = !!window.open(url, '_blank');
            }
            setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
            return opened;
        },

        cancelOpen: function (id) {
            var win = id ? pending[id] : null;
            if (id)
                delete pending[id];
            if (win && !win.closed)
                win.close();
        },

        open: function (base64, mime) {
            return window.fileSave.completeOpen('', base64, mime);
        }
    };
})();
