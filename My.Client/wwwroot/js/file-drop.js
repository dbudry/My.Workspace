// Drag-and-drop / paste file zone: forwards files to a hidden <input type="file"> or Blazor paste handler.
window.fileDropZone = {
  _handlers: {},

  init: function (dropZoneId, inputId, dotNetRef) {
    const zone = document.getElementById(dropZoneId);
    const input = document.getElementById(inputId);
    if (!zone || !input) return;

    this.dispose(dropZoneId);

    const setDragOver = function (on) {
      if (on) zone.classList.add('file-drop-zone--active');
      else zone.classList.remove('file-drop-zone--active');
    };

    const onDragEnter = function (e) {
      e.preventDefault();
      setDragOver(true);
    };
    const onDragOver = function (e) {
      e.preventDefault();
      setDragOver(true);
    };
    const onDragLeave = function (e) {
      e.preventDefault();
      if (!zone.contains(e.relatedTarget)) setDragOver(false);
    };
    const onDrop = function (e) {
      e.preventDefault();
      setDragOver(false);
      if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        input.files = e.dataTransfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
    };
    const onClick = function (e) {
      if (e.target.closest('button, a, input, label')) return;
      try {
        if (typeof input.showPicker === 'function') input.showPicker();
        else input.click();
      } catch (_) {
        input.click();
      }
    };
    const onPaste = function (e) {
      if (!dotNetRef || !dotNetRef.invokeMethodAsync) return;
      const items = e.clipboardData && e.clipboardData.items;
      if (!items) return;

      for (let i = 0; i < items.length; i++) {
        const item = items[i];
        if (!item.type || !item.type.startsWith('image/')) continue;
        const file = item.getAsFile();
        if (!file) continue;

        e.preventDefault();
        const reader = new FileReader();
        reader.onload = function () {
          const result = reader.result || '';
          const comma = String(result).indexOf(',');
          const base64 = comma >= 0 ? String(result).slice(comma + 1) : '';
          const fileName = file.name && file.name.trim() ? file.name : 'pasted-image.png';
          dotNetRef.invokeMethodAsync('HandlePastedFileAsync', fileName, file.type, base64, file.size)
            .catch(function (err) { console.warn('fileDropZone paste failed', err); });
        };
        reader.readAsDataURL(file);
        break;
      }
    };

    zone.addEventListener('dragenter', onDragEnter);
    zone.addEventListener('dragover', onDragOver);
    zone.addEventListener('dragleave', onDragLeave);
    zone.addEventListener('drop', onDrop);
    zone.addEventListener('click', onClick);
    zone.addEventListener('paste', onPaste);

    this._handlers[dropZoneId] = {
      zone: zone,
      listeners: [
        [zone, 'dragenter', onDragEnter],
        [zone, 'dragover', onDragOver],
        [zone, 'dragleave', onDragLeave],
        [zone, 'drop', onDrop],
        [zone, 'click', onClick],
        [zone, 'paste', onPaste]
      ]
    };
  },

  dispose: function (dropZoneId) {
    const entry = this._handlers[dropZoneId];
    if (!entry) return;
    entry.listeners.forEach(function (pair) {
      pair[0].removeEventListener(pair[1], pair[2]);
    });
    delete this._handlers[dropZoneId];
  }
};