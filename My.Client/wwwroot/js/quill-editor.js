// Lightweight Quill interop for the intranet page editor.
// Quill is lazy-loaded from /lib/quill/ only when the editor page opens.

window.quillEditor = {
  _instances: {},
  _dotNetRefs: {},
  _imageResize: {},
  _dropHandlers: {},
  _loadPromise: null,
  _iconsRegistered: false,
  _pasteSanitizerRegistered: false,
  _colorStyleProps: ['color', 'background', 'background-color', '-webkit-text-fill-color'],

  _registerIcons: function () {
    if (this._iconsRegistered || typeof Quill === 'undefined') return;
    const icons = Quill.import('ui/icons');
    icons['library-image'] = '<span class="ql-material-icon" aria-hidden="true">image</span>';
    icons['library-link'] = '<span class="ql-material-icon" aria-hidden="true">folder</span>';
    icons['html-code'] = '<span class="ql-material-icon" aria-hidden="true">code</span>';
    this._iconsRegistered = true;
  },

  _placeholderSrc: 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7',

  _getQuillElementId: function (quill) {
    if (!quill || !quill.container) return null;
    return quill.container.getAttribute('data-quill-id') || quill.container.id || null;
  },

  _isIntranetImageNode: function (img) {
    if (!img) return false;
    if (img.getAttribute('data-drive-file-id')) return true;
    if (img.getAttribute('data-intranet-media') === 'true') return true;
    var src = img.getAttribute('src') || '';
    return !!this._extractDriveFileIdFromSrc(src);
  },

  _isExternalImageSrc: function (src) {
    if (!src || src === this._placeholderSrc) return false;
    if (src.indexOf('data:') === 0) return true;
    if (src.indexOf('file:') === 0) return true;
    if (/^https?:\/\//i.test(src)) return !this._extractDriveFileIdFromSrc(src);
    return false;
  },

  _parseDataUrl: function (dataUrl) {
    if (!dataUrl || dataUrl.indexOf('data:') !== 0) return null;
    var comma = dataUrl.indexOf(',');
    if (comma < 0) return null;
    var header = dataUrl.slice(0, comma);
    var base64 = dataUrl.slice(comma + 1);
    var mimeMatch = header.match(/^data:([^;]+)/i);
    return {
      mimeType: mimeMatch ? mimeMatch[1] : 'image/png',
      base64: base64
    };
  },

  _looksLikeImageUrl: function (url) {
    if (!url) return false;
    var u = String(url).trim().toLowerCase();
    if (!/^https?:\/\//i.test(u)) return false;
    if (/\.(png|jpe?g|gif|webp|svg|bmp|ico)(\?|#|$)/i.test(u)) return true;
    if (u.indexOf('googleusercontent.com') >= 0) return true;
    if (u.indexOf('ggpht.com') >= 0) return true;
    if (u.indexOf('gstatic.com') >= 0 && u.indexOf('/images') >= 0) return true;
    return false;
  },

  _describeExternalImage: function (src, alt) {
    return {
      src: src,
      alt: alt || '',
      kind: src.indexOf('data:') === 0 ? 'data' : (src.indexOf('file:') === 0 ? 'file-url' : 'url')
    };
  },

  _analyzeTransferImages: function (transfer) {
    var self = this;
    var html = transfer && transfer.getData ? transfer.getData('text/html') : '';
    var result = {
      intranetOnly: false,
      externalImages: [],
      clipboardFile: null
    };

    var items = transfer && transfer.items ? transfer.items : null;
    if (items) {
      for (var i = 0; i < items.length; i++) {
        var item = items[i];
        if (item.type && item.type.indexOf('image/') === 0) {
          var file = item.getAsFile();
          if (file) {
            result.clipboardFile = file;
            break;
          }
        }
      }
    }

    if (html && html.trim()) {
      try {
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var body = doc.body;
        if (body) {
          var imgs = body.querySelectorAll('img');
          var hasExternal = false;

          for (var j = 0; j < imgs.length; j++) {
            var img = imgs[j];
            if (self._isIntranetImageNode(img)) continue;

            var src = img.getAttribute('src') || '';
            if (!self._isExternalImageSrc(src)) continue;

            hasExternal = true;
            result.externalImages.push(self._describeExternalImage(src, img.getAttribute('alt') || ''));
          }

          if (imgs.length > 0 && !hasExternal) {
            result.intranetOnly = true;
          }
        }
      } catch (err) {
        console.warn('quillEditor._analyzeTransferImages failed', err);
      }
    }

    if (!result.externalImages.length && transfer && transfer.getData) {
      var uriList = transfer.getData('text/uri-list') || transfer.getData('text/plain');
      if (uriList) {
        var lines = uriList.split('\n');
        for (var k = 0; k < lines.length; k++) {
          var line = lines[k].trim();
          if (!line || line.indexOf('#') === 0) continue;
          if (self._looksLikeImageUrl(line)) {
            result.externalImages.push(self._describeExternalImage(line, ''));
            break;
          }
        }
      }
    }

    return result;
  },

  _transferHasPendingImage: function (transfer) {
    if (!transfer) return false;
    var analysis = this._analyzeTransferImages(transfer);
    return !!((analysis.externalImages && analysis.externalImages.length) || analysis.clipboardFile);
  },

  _buildImagePayload: function (opts) {
    return {
      kind: opts.kind,
      sourceUrl: opts.sourceUrl || null,
      fileName: opts.fileName || null,
      mimeType: opts.mimeType || null,
      base64: opts.base64 || null,
      altText: opts.altText || null,
      canUpload: !!opts.canUpload,
      canLink: !!opts.canLink
    };
  },

  _handleImageTransfer: function (elementId, quill, range, analysis, html) {
    var self = this;
    var buildPayload = self._buildImagePayload;
    var images = analysis.externalImages || [];

    var finishWithFile = function (file, extra) {
      self._readFileAsBase64(file).then(function (base64) {
        var fileName = file.name && file.name.trim() ? file.name : 'pasted-image.png';
        self._notifyImagePaste(elementId, quill, range, buildPayload({
          kind: extra && extra.kind ? extra.kind : 'file',
          sourceUrl: extra && extra.sourceUrl ? extra.sourceUrl : null,
          fileName: fileName,
          mimeType: file.type || 'image/png',
          base64: base64,
          altText: extra && extra.altText ? extra.altText : null,
          canUpload: true,
          canLink: !!(extra && extra.canLink)
        }));
      }).catch(function (err) {
        console.warn('quillEditor image transfer file read failed', err);
      });
    };

    if (images.length === 1) {
      var ext = images[0];
      if (ext.kind === 'data') {
        var parsed = self._parseDataUrl(ext.src);
        self._notifyImagePaste(elementId, quill, range, buildPayload({
          kind: 'data',
          sourceUrl: ext.src,
          fileName: 'pasted-image.png',
          mimeType: parsed ? parsed.mimeType : 'image/png',
          base64: parsed ? parsed.base64 : null,
          altText: ext.alt,
          canUpload: !!(parsed && parsed.base64),
          canLink: false
        }));
        return;
      }

      if (analysis.clipboardFile) {
        finishWithFile(analysis.clipboardFile, {
          kind: 'url',
          sourceUrl: ext.src,
          altText: ext.alt,
          canLink: ext.kind === 'url'
        });
        return;
      }

      self._notifyImagePaste(elementId, quill, range, buildPayload({
        kind: ext.kind,
        sourceUrl: ext.src,
        fileName: null,
        mimeType: null,
        base64: null,
        altText: ext.alt,
        canUpload: false,
        canLink: ext.kind === 'url'
      }));
      return;
    }

    if (analysis.clipboardFile) {
      finishWithFile(analysis.clipboardFile, null);
    }
  },

  _routeImageTransfer: function (elementId, quill, range, transfer, analysis) {
    var self = this;
    var html = transfer && transfer.getData ? transfer.getData('text/html') : '';
    var images = analysis.externalImages || [];

    if (images.length === 1 || (images.length === 0 && analysis.clipboardFile)) {
      self._handleImageTransfer(elementId, quill, range, analysis, html);
      return;
    }

    if (images.length > 1 && html && html.trim()) {
      var sanitized = self._sanitizeRichHtml(html);
      var index = range ? range.index : quill.getLength();
      quill.clipboard.dangerouslyPasteHTML(index, sanitized, 'user');
      self._sanitizeEditorDom(quill.root);
      return;
    }

    if (analysis.clipboardFile) {
      self._handleImageTransfer(elementId, quill, range, analysis, html);
    }
  },

  _getDropIndex: function (quill, event) {
    try {
      var doc = quill.root.ownerDocument;
      var nativeRange = null;
      if (doc.caretRangeFromPoint) {
        nativeRange = doc.caretRangeFromPoint(event.clientX, event.clientY);
      } else if (doc.caretPositionFromPoint) {
        var pos = doc.caretPositionFromPoint(event.clientX, event.clientY);
        if (pos) {
          nativeRange = doc.createRange();
          nativeRange.setStart(pos.offsetNode, pos.offset);
          nativeRange.collapse(true);
        }
      }
      if (!nativeRange) return null;

      var blot = Quill.find(nativeRange.startContainer, true);
      if (!blot) return null;
      if (blot === quill.scroll) return quill.getLength();
      return quill.getIndex(blot);
    } catch (e) {
      return null;
    }
  },

  _initDropHandler: function (elementId, quill) {
    var self = this;
    var container = quill.container;
    if (!container) return;

    self._destroyDropHandler(elementId);

    var onDragOver = function (e) {
      if (!self._transferHasPendingImage(e.dataTransfer)) return;
      e.preventDefault();
    };

    var onDrop = function (e) {
      if (e.defaultPrevented) return;

      var dataTransfer = e.dataTransfer;
      if (!dataTransfer) return;

      var analysis = self._analyzeTransferImages(dataTransfer);
      if (analysis.intranetOnly) return;
      if (!(analysis.externalImages && analysis.externalImages.length) && !analysis.clipboardFile) return;

      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();

      var dropIndex = self._getDropIndex(quill, e);
      if (dropIndex == null) {
        var sel = quill.getSelection(true);
        dropIndex = sel ? sel.index : quill.getLength();
      }

      quill.setSelection(dropIndex, 0, 'silent');
      var range = { index: dropIndex, length: 0 };
      self._routeImageTransfer(elementId, quill, range, dataTransfer, analysis);
    };

    container.addEventListener('dragover', onDragOver);
    container.addEventListener('drop', onDrop, true);

    self._dropHandlers[elementId] = {
      editor: container,
      onDragOver: onDragOver,
      onDrop: onDrop
    };
  },

  _destroyDropHandler: function (elementId) {
    var entry = this._dropHandlers[elementId];
    if (!entry) return;

    try {
      entry.editor.removeEventListener('dragover', entry.onDragOver);
      entry.editor.removeEventListener('drop', entry.onDrop, true);
    } catch (e) {
      console.warn('quillEditor._destroyDropHandler failed', e);
    }

    delete this._dropHandlers[elementId];
  },

  fetchImageAsBase64: async function (url) {
    try {
      var resp = await fetch(url, { mode: 'cors', credentials: 'omit' });
      if (!resp.ok) return null;
      var blob = await resp.blob();
      if (!blob || !blob.type || blob.type.indexOf('image/') !== 0) return null;

      return await new Promise(function (resolve, reject) {
        var reader = new FileReader();
        reader.onload = function () {
          var result = reader.result || '';
          var comma = String(result).indexOf(',');
          resolve({
            base64: comma >= 0 ? String(result).slice(comma + 1) : '',
            mimeType: blob.type
          });
        };
        reader.onerror = function () { reject(reader.error || new Error('read failed')); };
        reader.readAsDataURL(blob);
      });
    } catch (e) {
      console.warn('quillEditor.fetchImageAsBase64 failed', e);
      return null;
    }
  },

  _readFileAsBase64: function (file) {
    return new Promise(function (resolve, reject) {
      var reader = new FileReader();
      reader.onload = function () {
        var result = reader.result || '';
        var comma = String(result).indexOf(',');
        resolve(comma >= 0 ? String(result).slice(comma + 1) : '');
      };
      reader.onerror = function () { reject(reader.error || new Error('read failed')); };
      reader.readAsDataURL(file);
    });
  },

  _notifyImagePaste: function (elementId, quill, range, payload) {
    var dotNetRef = this._dotNetRefs[elementId];
    if (!dotNetRef || !dotNetRef.invokeMethodAsync) {
      console.warn('quillEditor: no .NET reference for image transfer', elementId);
      return;
    }
    if (range) this._instances[elementId + ':range'] = range;
    dotNetRef.invokeMethodAsync('OnImagePasteDetected', payload).catch(function (e) {
      console.warn('quillEditor image transfer invoke failed', e);
    });
  },

  _registerPasteSanitizer: function () {
    if (this._pasteSanitizerRegistered || typeof Quill === 'undefined') return;

    const Clipboard = Quill.import('modules/clipboard');
    const self = this;

    class IntranetClipboard extends Clipboard {
      onPaste(e) {
        if (e.defaultPrevented) return;
        const range = this.quill.getSelection(true);
        const clipboard = e.clipboardData;
        if (!clipboard || !range) return super.onPaste(e);

        const elementId = self._getQuillElementId(this.quill);
        const html = clipboard.getData('text/html');
        const analysis = self._analyzeTransferImages(clipboard);

        if (analysis.intranetOnly && html && html.trim()) {
          e.preventDefault();
          const sanitized = self._sanitizeRichHtml(html);
          this.quill.clipboard.dangerouslyPasteHTML(range.index, sanitized, 'user');
          self._sanitizeEditorDom(this.quill.root);
          self._scheduleRestoreIntranetImageAttrs(this.quill.root, sanitized);
          return;
        }

        if (elementId && ((analysis.externalImages && analysis.externalImages.length) || analysis.clipboardFile)) {
          e.preventDefault();
          self._routeImageTransfer(elementId, this.quill, range, clipboard, analysis);
          return;
        }

        if (html && html.trim()) {
          e.preventDefault();
          const sanitized = self._sanitizeRichHtml(html);
          this.quill.clipboard.dangerouslyPasteHTML(range.index, sanitized, 'user');
          self._sanitizeEditorDom(this.quill.root);
          self._scheduleRestoreIntranetImageAttrs(this.quill.root, sanitized);
          return;
        }

        return super.onPaste(e);
      }
    }

    Quill.register('modules/clipboard', IntranetClipboard, true);
    this._pasteSanitizerRegistered = true;
  },

  _stripColorStylesFromElement: function (el) {
    if (!el || el.nodeType !== Node.ELEMENT_NODE) return;

    if (el.hasAttribute('color')) el.removeAttribute('color');
    if (el.hasAttribute('bgcolor')) el.removeAttribute('bgcolor');

    if (el.style && el.style.length) {
      const props = this._colorStyleProps;
      for (let i = 0; i < props.length; i++) {
        el.style.removeProperty(props[i]);
      }
      if (!el.getAttribute('style') || !el.getAttribute('style').trim()) {
        el.removeAttribute('style');
      }
    }

    if (el.classList) {
      Array.from(el.classList).forEach(function (cls) {
        if (cls.indexOf('ql-color-') === 0 || cls.indexOf('ql-bg-') === 0) {
          el.classList.remove(cls);
        }
      });
    }
  },

  _sanitizeEditorDom: function (root) {
    if (!root || !root.querySelectorAll) return;
    const self = this;
    root.querySelectorAll('[style], [color], [bgcolor], font').forEach(function (el) {
      self._stripColorStylesFromElement(el);
    });
  },

  _extractDriveFileIdFromSrc: function (src) {
    if (!src) return null;
    var match = src.match(/[?&]id=([^&]+)/i);
    if (match) return decodeURIComponent(match[1]);
    match = src.match(/\/file\/d\/([^/]+)/i);
    return match ? match[1] : null;
  },

  _parseIntranetImagesFromHtml: function (html) {
    if (!html) return [];
    try {
      var doc = new DOMParser().parseFromString(html, 'text/html');
      var body = doc.body;
      if (!body) return [];
      var imgs = body.querySelectorAll('img');
      var result = [];
      for (var i = 0; i < imgs.length; i++) {
        var img = imgs[i];
        var driveId = img.getAttribute('data-drive-file-id');
        if (!driveId) driveId = this._extractDriveFileIdFromSrc(img.getAttribute('src'));
        var srcAttr = img.getAttribute('src') || '';
        var externalSrc = img.getAttribute('data-external-src')
          || (img.getAttribute('data-external-image') === 'true' ? srcAttr : '');
        if (!externalSrc && !driveId && /^https?:\/\//i.test(srcAttr) && !this._extractDriveFileIdFromSrc(srcAttr)) {
          externalSrc = srcAttr;
        }

        result.push({
          driveFileId: driveId,
          externalSrc: externalSrc || '',
          referrerPolicy: img.getAttribute('referrerpolicy') || 'no-referrer',
          alt: img.getAttribute('alt') || '',
          style: img.getAttribute('style') || '',
          width: img.getAttribute('width') || '',
          height: img.getAttribute('height') || '',
          className: img.getAttribute('class') || ''
        });
      }
      return result;
    } catch (e) {
      return [];
    }
  },

  _restoreIntranetImageAttrs: function (quillRoot, sourceHtml) {
    if (!quillRoot || !sourceHtml) return;
    var sourceImgs = this._parseIntranetImagesFromHtml(sourceHtml);
    if (!sourceImgs.length) return;

    var placeholder = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
    var liveImgs = quillRoot.querySelectorAll('img');
    var usedSource = new Set();

    for (var i = 0; i < liveImgs.length; i++) {
      var live = liveImgs[i];
      var existingDriveId = live.getAttribute('data-drive-file-id');
      if (existingDriveId) {
        if (window.intranetMedia && window.intranetMedia._markDriveImagePending) {
          window.intranetMedia._markDriveImagePending(live);
        } else {
          live.classList.add('intranet-pending-media');
          live.dataset.intranetHydrated = 'false';
          if ((live.getAttribute('src') || '') !== placeholder) {
            live.setAttribute('src', placeholder);
          }
        }
        continue;
      }
      if (live.getAttribute('data-external-image') === 'true'
          && live.getAttribute('referrerpolicy')
          && live.getAttribute('data-external-src')
          && !live.getAttribute('data-drive-file-id')) continue;

      var src = null;
      if (i < sourceImgs.length && !usedSource.has(i)
          && (sourceImgs[i].driveFileId || sourceImgs[i].externalSrc)) {
        src = sourceImgs[i];
        usedSource.add(i);
      } else {
        var alt = (live.getAttribute('alt') || '').trim();
        for (var j = 0; j < sourceImgs.length; j++) {
          if (usedSource.has(j) || (!sourceImgs[j].driveFileId && !sourceImgs[j].externalSrc)) continue;
          var sourceAlt = (sourceImgs[j].alt || '').trim();
          if (alt && sourceAlt && alt === sourceAlt) {
            src = sourceImgs[j];
            usedSource.add(j);
            break;
          }
        }
      }

      if (!src) continue;

      if (src.driveFileId) {
        live.setAttribute('data-drive-file-id', src.driveFileId);
        live.setAttribute('data-intranet-media', 'true');
        live.setAttribute('src', placeholder);
        if (src.externalSrc) {
          live.setAttribute('data-external-src', src.externalSrc);
          live.setAttribute('data-external-image', 'true');
          live.setAttribute('referrerpolicy', src.referrerPolicy || 'no-referrer');
        }
        live.classList.add('intranet-pending-media');
        live.dataset.intranetHydrated = 'false';
      } else if (src.externalSrc) {
        live.setAttribute('src', src.externalSrc);
        live.setAttribute('data-external-src', src.externalSrc);
        live.setAttribute('data-external-image', 'true');
        live.setAttribute('referrerpolicy', src.referrerPolicy || 'no-referrer');
      } else {
        continue;
      }

      if (src.alt) live.setAttribute('alt', src.alt);
      if (src.style) live.setAttribute('style', src.style);
      if (src.width) live.setAttribute('width', src.width);
      if (src.height) live.setAttribute('height', src.height);
      if (src.className) {
        src.className.split(/\s+/).forEach(function (cls) {
          if (cls) live.classList.add(cls);
        });
      }
    }
  },

  _scheduleRestoreIntranetImageAttrs: function (quillRoot, sourceHtml) {
    if (!quillRoot || !sourceHtml) return;
    var self = this;
    self._restoreIntranetImageAttrs(quillRoot, sourceHtml);
    requestAnimationFrame(function () {
      self._restoreIntranetImageAttrs(quillRoot, sourceHtml);
      requestAnimationFrame(function () {
        self._restoreIntranetImageAttrs(quillRoot, sourceHtml);
      });
    });
  },

  _sanitizeRichHtml: function (html) {
    if (!html) return html;
    try {
      const doc = new DOMParser().parseFromString(html, 'text/html');
      const body = doc.body;
      if (!body) return html;

      const self = this;
      const walk = function (node) {
        if (node.nodeType !== Node.ELEMENT_NODE) return;
        self._stripColorStylesFromElement(node);
        for (let i = 0; i < node.childNodes.length; i++) {
          walk(node.childNodes[i]);
        }
      };
      walk(body);
      return body.innerHTML;
    } catch (e) {
      return html;
    }
  },

  _stripColorFromDelta: function (delta) {
    if (!delta || !delta.ops) return delta;
    try {
      const Delta = Quill.import('delta');
      const ops = delta.ops.map(function (op) {
        if (!op.attributes) return op;
        const attrs = Object.assign({}, op.attributes);
        delete attrs.color;
        delete attrs.background;
        const keys = Object.keys(attrs);
        if (!keys.length) return { insert: op.insert };
        return Object.assign({}, op, { attributes: attrs });
      });
      return new Delta(ops);
    } catch (e) {
      return delta;
    }
  },

  _initClipboardMatchers: function (quill) {
    const self = this;
    quill.clipboard.addMatcher(Node.ELEMENT_NODE, function (node, delta) {
      if (node && node.style) self._stripColorStylesFromElement(node);
      return self._stripColorFromDelta(delta);
    });
  },

  _invokeDotNet: function (elementId, methodName) {
    const dotNetRef = this._dotNetRefs[elementId];
    if (!dotNetRef || !dotNetRef.invokeMethodAsync) {
      console.warn('quillEditor: no .NET reference for', elementId);
      return;
    }
    dotNetRef.invokeMethodAsync(methodName).catch(function (e) {
      console.warn('quillEditor invoke failed:', methodName, e);
    });
  },

  _loadStylesheet: function (href) {
    return new Promise(function (resolve, reject) {
      if (document.querySelector('link[data-quill-css="true"]')) {
        resolve();
        return;
      }
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = href;
      link.setAttribute('data-quill-css', 'true');
      link.onload = function () { resolve(); };
      link.onerror = function () { reject(new Error('Failed to load ' + href)); };
      document.head.appendChild(link);
    });
  },

  _loadScript: function (src) {
    return new Promise(function (resolve, reject) {
      if (document.querySelector('script[data-quill-js="true"]')) {
        if (typeof Quill !== 'undefined') { resolve(); return; }
      }
      const script = document.createElement('script');
      script.src = src;
      script.setAttribute('data-quill-js', 'true');
      script.onload = function () { resolve(); };
      script.onerror = function () { reject(new Error('Failed to load ' + src)); };
      document.head.appendChild(script);
    });
  },

  ensureLoaded: function () {
    if (typeof Quill !== 'undefined') {
      this._registerPasteSanitizer();
      return Promise.resolve(true);
    }
    if (this._loadPromise) return this._loadPromise;

    const self = this;
    this._loadPromise = this._loadStylesheet('lib/quill/quill.snow.css')
      .then(function () { return self._loadScript('lib/quill/quill.min.js'); })
      .then(function () {
        if (typeof Quill !== 'undefined') self._registerPasteSanitizer();
        return typeof Quill !== 'undefined';
      })
      .catch(function (e) {
        console.error('quillEditor.ensureLoaded failed', e);
        self._loadPromise = null;
        return false;
      });

    return this._loadPromise;
  },

  isMounted: function (elementId) {
    return !!this._instances[elementId];
  },

  destroy: function (elementId) {
    try {
      const existing = this._instances[elementId];
      if (!existing) return;
      const el = document.getElementById(elementId);
      if (el) {
        const wrapper = el.parentElement;
        const toolbar = wrapper && wrapper.querySelector('.ql-toolbar');
        if (toolbar) toolbar.remove();
        el.innerHTML = '';
        el.classList.remove('ql-container', 'ql-snow');
        el.removeAttribute('data-quill-id');
      }
      this._destroyImageResize(elementId);
      this._destroyDropHandler(elementId);
      delete this._instances[elementId];
      delete this._instances[elementId + ':range'];
      delete this._dotNetRefs[elementId];
    } catch (e) {
      console.warn('quillEditor.destroy failed', e);
    }
  },

  init: async function (elementId, dotNetRef) {
    try {
      const ready = await this.ensureLoaded();
      if (!ready) return false;

      this.destroy(elementId);
      const el = document.getElementById(elementId);
      if (!el) return false;

      this._registerIcons();
      this._dotNetRefs[elementId] = dotNetRef;
      const self = this;

      const quill = new Quill('#' + elementId, {
        theme: 'snow',
        modules: {
          toolbar: {
            container: [
              [{ header: [2, 3, false] }],
              ['bold', 'italic', 'underline', 'strike'],
              [{ list: 'ordered' }, { list: 'bullet' }],
              [{ indent: '-1' }, { indent: '+1' }],
              [{ align: [] }],
              ['link'],
              ['library-image', 'library-link', 'html-code'],
              ['clean']
            ],
            handlers: {
              // Override Quill's default link tooltip — it uses <a href=""> previews that
              // can reload the page inside Blazor WASM. Route to our insert dialog instead.
              'link': function () {
                self._invokeDotNet(elementId, 'OpenWebLinkInsertDialog');
              },
              'library-image': function () {
                self._invokeDotNet(elementId, 'OpenImageInsertDialog');
              },
              'library-link': function () {
                self._invokeDotNet(elementId, 'OpenLibraryLinkInsertDialog');
              },
              'html-code': function () {
                self._invokeDotNet(elementId, 'OpenHtmlCodeDialog');
              },
              // Ensure indent applies to the image currently selected for resize, not a
              // stale Quill range left from a previous image click.
              indent: function (value) {
                var q = this.quill;
                self._formatWithSelectedImage(elementId, q, function () {
                  q.format('indent', value, 'user');
                });
              }
            }
          }
        },
        placeholder: 'Start writing page content…'
      });

      // Belt-and-suspenders: block anchor navigation inside the Quill toolbar/tooltips.
      const toolbarEl = quill.getModule('toolbar');
      if (toolbarEl && toolbarEl.container) {
        toolbarEl.container.addEventListener('click', function (e) {
          const anchor = e.target && e.target.closest ? e.target.closest('a') : null;
          if (anchor) {
            e.preventDefault();
            e.stopPropagation();
          }
        }, true);
      }

      quill.root.classList.add('intranet-rich-content');
      this._initClipboardMatchers(quill);
      this._instances[elementId] = quill;
      this._initDropHandler(elementId, quill);
      this._initImageResize(elementId, quill);
      el.setAttribute('data-quill-id', elementId);
      return true;
    } catch (e) {
      console.warn('quillEditor.init failed', e);
      return false;
    }
  },

  getHtml: function (elementId) {
    try {
      const quill = this._instances[elementId];
      if (!quill) return '';

      // Quill's innerHTML omits custom data-* attrs; copy them from the live DOM before serializing.
      const root = quill.root;
      const clone = root.cloneNode(true);
      const origImgs = root.querySelectorAll('img');
      const cloneImgs = clone.querySelectorAll('img');
      for (let i = 0; i < origImgs.length; i++) {
        const orig = origImgs[i];
        const img = cloneImgs[i];
        if (!img) continue;
        const driveId = orig.getAttribute('data-drive-file-id');
        if (driveId) {
          img.setAttribute('data-drive-file-id', driveId);
          img.setAttribute('data-intranet-media', 'true');
          img.setAttribute('src', 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7');
          var extOnDrive = orig.getAttribute('data-external-src');
          if (extOnDrive) {
            img.setAttribute('data-external-src', extOnDrive);
            img.setAttribute('data-external-image', 'true');
            img.setAttribute('referrerpolicy', orig.getAttribute('referrerpolicy') || 'no-referrer');
          }
        } else {
          var externalSrc = orig.getAttribute('data-external-src') || '';
          var liveSrc = orig.getAttribute('src') || '';
          if (orig.getAttribute('data-external-image') === 'true' || externalSrc) {
            var hotlinkSrc = externalSrc || liveSrc;
            if (hotlinkSrc) {
              img.setAttribute('src', hotlinkSrc);
              img.setAttribute('data-external-src', hotlinkSrc);
              img.setAttribute('data-external-image', 'true');
              img.setAttribute('referrerpolicy', orig.getAttribute('referrerpolicy') || 'no-referrer');
            }
          } else if (liveSrc) {
            img.setAttribute('src', liveSrc);
          }
          if (orig.getAttribute('data-intranet-media')) {
            img.setAttribute('data-intranet-media', 'true');
          }
        }
        var alt = orig.getAttribute('alt');
        if (alt) img.setAttribute('alt', alt);
        var style = orig.getAttribute('style');
        if (style) img.setAttribute('style', style);
        var width = orig.getAttribute('width');
        if (width) img.setAttribute('width', width);
        var height = orig.getAttribute('height');
        if (height) img.setAttribute('height', height);
        var cls = orig.getAttribute('class');
        if (cls) img.setAttribute('class', cls);
      }
      return this._sanitizeRichHtml(clone.innerHTML);
    } catch (e) {
      return '';
    }
  },

  setHtml: function (elementId, html) {
    try {
      const quill = this._instances[elementId];
      if (!quill) return;
      const safe = html || '';
      if (safe.trim() === '') {
        quill.setText('');
        return;
      }
      quill.clipboard.dangerouslyPasteHTML(this._sanitizeRichHtml(safe));
      this._sanitizeEditorDom(quill.root);
      this._scheduleRestoreIntranetImageAttrs(quill.root, safe);
    } catch (e) {
      console.warn('quillEditor.setHtml failed', e);
    }
  },

  getSelectedText: function (elementId) {
    try {
      const quill = this._instances[elementId];
      if (!quill) return '';
      const range = quill.getSelection();
      if (!range || range.length === 0) return '';
      return quill.getText(range.index, range.length).trim();
    } catch (e) {
      return '';
    }
  },

  saveSelection: function (elementId) {
    try {
      const quill = this._instances[elementId];
      if (!quill) return;
      this._instances[elementId + ':range'] = quill.getSelection();
    } catch (e) { }
  },

  restoreSelection: function (elementId) {
    try {
      const quill = this._instances[elementId];
      const range = this._instances[elementId + ':range'];
      if (!quill) return;
      quill.focus();
      if (range) quill.setSelection(range.index, range.length, 'user');
      else quill.setSelection(quill.getLength(), 0, 'user');
    } catch (e) { }
  },

  insertHtml: function (elementId, html) {
    try {
      const quill = this._instances[elementId];
      if (!quill || !html) return false;
      this.restoreSelection(elementId);
      const range = quill.getSelection(true);
      const index = range ? range.index : quill.getLength();
      quill.clipboard.dangerouslyPasteHTML(index, this._sanitizeRichHtml(html));
      this._sanitizeEditorDom(quill.root);
      this._scheduleRestoreIntranetImageAttrs(quill.root, html);
      return true;
    } catch (e) {
      console.warn('quillEditor.insertHtml failed', e);
      return false;
    }
  },

  // Quill strips data-drive-file-id on embed; route through insertHtml + attr restore instead.
  insertIntranetImage: function (elementId, driveFileId, alt, placeholderSrc) {
    if (!driveFileId) return false;
    var src = placeholderSrc || this._placeholderSrc;
    var safeAlt = String(alt || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
    var safeId = String(driveFileId).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
    var html = '<p><img src="' + src + '" alt="' + safeAlt + '" data-drive-file-id="' + safeId
      + '" data-intranet-media="true" style="max-width:100%;height:auto;" /></p>';
    return this.insertHtml(elementId, html);
  },

  applyExternalImagePreview: function (elementId, externalSrc, base64, mimeType) {
    try {
      var quill = this._instances[elementId];
      if (!quill || !externalSrc || !base64) return false;

      var binary = atob(base64);
      var bytes = new Uint8Array(binary.length);
      for (var b = 0; b < binary.length; b++) bytes[b] = binary.charCodeAt(b);

      var blob = new Blob([bytes], { type: mimeType || 'image/png' });
      var blobUrl = URL.createObjectURL(blob);
      var imgs = quill.root.querySelectorAll('img[data-external-src]');

      for (var i = imgs.length - 1; i >= 0; i--) {
        var img = imgs[i];
        if (img.getAttribute('data-external-src') !== externalSrc) continue;
        img.src = blobUrl;
        img.dataset.externalPreview = 'true';
        img.setAttribute('referrerpolicy', 'no-referrer');
        return true;
      }

      URL.revokeObjectURL(blobUrl);
      return false;
    } catch (e) {
      console.warn('quillEditor.applyExternalImagePreview failed', e);
      return false;
    }
  },

  hydrateIntranetImages: async function (elementId, accessToken, apiBaseUrl) {
    try {
      const quill = this._instances[elementId];
      if (!quill || !window.intranetMedia || !quill.root) return false;
      await window.intranetMedia.hydrate(quill.root, accessToken, apiBaseUrl);
      return true;
    } catch (e) {
      console.warn('quillEditor.hydrateIntranetImages failed', e);
      return false;
    }
  },

  focus: function (elementId) {
    try {
      const quill = this._instances[elementId];
      if (quill) quill.focus();
    } catch (e) { }
  },

  _initImageResize: function (elementId, quill) {
    const self = this;
    const container = quill.container;
    const editor = quill.root;
    if (!container || !editor) return;

    container.classList.add('intranet-quill-resize-container');

    const overlay = document.createElement('div');
    overlay.className = 'intranet-img-resize-overlay';
    overlay.setAttribute('aria-hidden', 'true');
    overlay.style.display = 'none';

    const handle = document.createElement('div');
    handle.className = 'intranet-img-resize-handle';
    handle.setAttribute('title', 'Drag to resize');
    overlay.appendChild(handle);
    container.appendChild(overlay);

    const state = {
      quill: quill,
      container: container,
      editor: editor,
      overlay: overlay,
      handle: handle,
      selectedImg: null,
      dragging: false
    };
    this._imageResize[elementId] = state;

    const reposition = function () {
      self._positionImageResizeOverlay(state);
    };

    const onEditorClick = function (e) {
      if (state.dragging) return;
      const img = e.target && e.target.tagName === 'IMG' ? e.target : null;
      if (img && editor.contains(img)) {
        // Don't stopPropagation before selection sync — but do prevent default
        // navigation on linked images. Selection must update so toolbar formats
        // (indent, align) target this image, not the previous caret.
        e.preventDefault();
        self._selectResizeImage(elementId, img);
        return;
      }
      if (!e.target.closest || !e.target.closest('.intranet-img-resize-overlay')) {
        self._deselectResizeImage(elementId);
      }
    };

    const onDocMouseDown = function (e) {
      if (state.dragging) return;
      if (!state.selectedImg) return;
      if (e.target === handle || (handle.contains && handle.contains(e.target))) return;
      if (editor.contains(e.target)) return;
      self._deselectResizeImage(elementId);
    };

    const onHandleMouseDown = function (e) {
      if (!state.selectedImg) return;
      e.preventDefault();
      e.stopPropagation();
      state.dragging = true;

      const img = state.selectedImg;
      const startX = e.clientX;
      const startRect = img.getBoundingClientRect();
      const startWidth = startRect.width;
      const ratio = img.naturalWidth > 0 && img.naturalHeight > 0
        ? img.naturalHeight / img.naturalWidth
        : (startRect.height / startWidth) || 1;
      const maxWidth = Math.max(40, editor.clientWidth - 8);

      const onMove = function (moveEvent) {
        moveEvent.preventDefault();
        let newWidth = Math.round(startWidth + (moveEvent.clientX - startX));
        newWidth = Math.max(40, Math.min(maxWidth, newWidth));
        const newHeight = Math.round(newWidth * ratio);
        img.style.maxWidth = '100%';
        img.style.width = newWidth + 'px';
        img.style.height = newHeight + 'px';
        img.removeAttribute('width');
        img.removeAttribute('height');
        reposition();
      };

      const onUp = function () {
        state.dragging = false;
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
        reposition();
      };

      document.addEventListener('mousemove', onMove);
      document.addEventListener('mouseup', onUp);
    };

    editor.addEventListener('click', onEditorClick);
    editor.addEventListener('scroll', reposition, { passive: true });
    handle.addEventListener('mousedown', onHandleMouseDown);
    document.addEventListener('mousedown', onDocMouseDown);
    window.addEventListener('resize', reposition);

    state._reposition = reposition;
    state._onEditorClick = onEditorClick;
    state._onDocMouseDown = onDocMouseDown;
    state._onHandleMouseDown = onHandleMouseDown;
  },

  _positionImageResizeOverlay: function (state) {
    if (!state || !state.selectedImg || !state.overlay) return;
    const img = state.selectedImg;
    const containerRect = state.container.getBoundingClientRect();
    const imgRect = img.getBoundingClientRect();
    state.overlay.style.display = 'block';
    state.overlay.style.left = (imgRect.left - containerRect.left) + 'px';
    state.overlay.style.top = (imgRect.top - containerRect.top) + 'px';
    state.overlay.style.width = imgRect.width + 'px';
    state.overlay.style.height = imgRect.height + 'px';
  },

  _selectResizeImage: function (elementId, img) {
    const state = this._imageResize[elementId];
    if (!state || !img) return;

    // Always re-sync Quill's selection — even when the same img is already selected —
    // so a later toolbar click (indent/align) cannot keep applying to a stale range
    // from a previous image.
    if (state.selectedImg !== img) {
      this._deselectResizeImage(elementId, false);
      state.selectedImg = img;
      img.classList.add('intranet-img-selected');
      if (state._reposition) {
        img.addEventListener('load', state._reposition, { once: true });
      }
    }

    this._syncQuillSelectionToImage(state, img);
    this._positionImageResizeOverlay(state);
  },

  // Point Quill's caret/range at the image blot so block formats (indent ±, align)
  // apply to that image's line. The resize overlay alone does not update selection;
  // without this, indent "tab over" keeps hitting the first image selected.
  _syncQuillSelectionToImage: function (state, img) {
    try {
      const quill = state && state.quill;
      if (!quill || !img) return;

      // Prefer live DOM node currently in the editor (survives format rewrites).
      let target = img;
      if (!quill.root.contains(target)) {
        // Stale node after indent rewrote the DOM — try matching by attributes.
        target = this._findMatchingEditorImage(quill, img) || img;
        if (state.selectedImg !== target && quill.root.contains(target)) {
          if (state.selectedImg) state.selectedImg.classList.remove('intranet-img-selected');
          state.selectedImg = target;
          target.classList.add('intranet-img-selected');
        }
      }

      const blot = typeof Quill !== 'undefined' && Quill.find ? Quill.find(target) : null;
      if (!blot) return;

      const index = quill.getIndex(blot);
      if (typeof index !== 'number' || index < 0) return;

      // Length 1 selects the embed so line-level formats bind to its block.
      quill.setSelection(index, 1, 'silent');
    } catch (e) {
      console.warn('quillEditor._syncQuillSelectionToImage failed', e);
    }
  },

  _findMatchingEditorImage: function (quill, staleImg) {
    if (!quill || !quill.root || !staleImg) return null;
    const driveId = staleImg.getAttribute('data-drive-file-id');
    const src = staleImg.getAttribute('src') || '';
    const imgs = quill.root.querySelectorAll('img');
    for (let i = 0; i < imgs.length; i++) {
      const candidate = imgs[i];
      if (driveId && candidate.getAttribute('data-drive-file-id') === driveId) return candidate;
    }
    if (src) {
      for (let j = 0; j < imgs.length; j++) {
        if ((imgs[j].getAttribute('src') || '') === src) return imgs[j];
      }
    }
    return null;
  },

  // Run a toolbar format after pointing selection at the resize-selected image,
  // then re-bind selectedImg if the DOM node was rewritten by the format.
  _formatWithSelectedImage: function (elementId, quill, applyFormat) {
    try {
      const state = this._imageResize[elementId];
      if (state && state.selectedImg) {
        this._syncQuillSelectionToImage(state, state.selectedImg);
      }
      applyFormat();
      if (state && state.selectedImg) {
        const self = this;
        // Format may replace the line DOM; refresh selection + overlay next frame.
        requestAnimationFrame(function () {
          const live = self._findMatchingEditorImage(quill, state.selectedImg) || state.selectedImg;
          if (live && quill.root.contains(live)) {
            self._selectResizeImage(elementId, live);
          }
        });
      }
    } catch (e) {
      console.warn('quillEditor._formatWithSelectedImage failed', e);
      try { applyFormat(); } catch (e2) { /* ignore */ }
    }
  },

  _deselectResizeImage: function (elementId, hideOverlay) {
    const state = this._imageResize[elementId];
    if (!state) return;

    if (state.selectedImg) {
      state.selectedImg.classList.remove('intranet-img-selected');
      state.selectedImg = null;
    }
    if (hideOverlay !== false && state.overlay) {
      state.overlay.style.display = 'none';
    }
  },

  _destroyImageResize: function (elementId) {
    const state = this._imageResize[elementId];
    if (!state) return;

    try {
      if (state.editor && state._onEditorClick) {
        state.editor.removeEventListener('click', state._onEditorClick);
      }
      if (state.editor && state._reposition) {
        state.editor.removeEventListener('scroll', state._reposition);
      }
      if (state.handle && state._onHandleMouseDown) {
        state.handle.removeEventListener('mousedown', state._onHandleMouseDown);
      }
      if (state._onDocMouseDown) {
        document.removeEventListener('mousedown', state._onDocMouseDown);
      }
      if (state._reposition) {
        window.removeEventListener('resize', state._reposition);
      }
      if (state.selectedImg) {
        state.selectedImg.classList.remove('intranet-img-selected');
      }
      if (state.overlay && state.overlay.parentNode) {
        state.overlay.parentNode.removeChild(state.overlay);
      }
      if (state.container) {
        state.container.classList.remove('intranet-quill-resize-container');
      }
    } catch (e) {
      console.warn('quillEditor._destroyImageResize failed', e);
    }

    delete this._imageResize[elementId];
  }
};