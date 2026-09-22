window.signaturePad = {
    init: function (canvas) {
        if (!canvas || canvas._sigBound) return;
        canvas._sigBound = true;
        var ctx = canvas.getContext('2d');
        ctx.strokeStyle = '#111';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        var drawing = false;

        var pos = function (e) {
            var r = canvas.getBoundingClientRect();
            return { x: (e.clientX - r.left) * (canvas.width / r.width), y: (e.clientY - r.top) * (canvas.height / r.height) };
        };

        canvas.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            drawing = true;
            canvas.setPointerCapture(e.pointerId);
            var p = pos(e);
            ctx.beginPath();
            ctx.moveTo(p.x, p.y);
        });
        canvas.addEventListener('pointermove', function (e) {
            if (!drawing) return;
            var p = pos(e);
            ctx.lineTo(p.x, p.y);
            ctx.stroke();
        });
        canvas.addEventListener('pointerup', function () { drawing = false; });
        canvas.addEventListener('pointercancel', function () { drawing = false; });
    },

    clear: function (canvas) {
        if (!canvas) return;
        var ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
    },

    toPng: function (canvas) {
        if (!canvas) return '';
        var blank = document.createElement('canvas');
        blank.width = canvas.width;
        blank.height = canvas.height;
        if (canvas.toDataURL() === blank.toDataURL())
            return '';
        var dataUrl = canvas.toDataURL('image/png');
        var comma = dataUrl.indexOf(',');
        return comma < 0 ? '' : dataUrl.substring(comma + 1);
    }
};
