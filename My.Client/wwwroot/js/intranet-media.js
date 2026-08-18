// Hydrates intranet Drive images that cannot load via public Drive URLs.
// img tags use data-drive-file-id; this module fetches via the authenticated API and sets blob URLs.

window.intranetMedia = {
  _blobUrls: new Map(),

  _extractDriveFileId: function (img) {
    var id = img.getAttribute('data-drive-file-id');
    if (id) return id;

    var src = img.getAttribute('src') || '';
    var match = src.match(/[?&]id=([^&]+)/i);
    if (match) {
      id = decodeURIComponent(match[1]);
      img.setAttribute('data-drive-file-id', id);
      img.setAttribute('data-intranet-media', 'true');
      return id;
    }

    match = src.match(/\/file\/d\/([^/]+)/i);
    if (match) {
      id = match[1];
      img.setAttribute('data-drive-file-id', id);
      img.setAttribute('data-intranet-media', 'true');
      return id;
    }

    return null;
  },

  _resolveContainer: function (containerOrId) {
    if (!containerOrId) return null;
    if (typeof containerOrId === 'string') {
      return document.getElementById(containerOrId) || document.querySelector(containerOrId);
    }
    return containerOrId;
  },

  _placeholderSrc: 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7',

  _markDriveImagePending: function (img) {
    if (!img || !img.getAttribute('data-drive-file-id')) return;

    var src = img.getAttribute('src') || '';
    if (img.dataset.intranetHydrated === 'true' && src.indexOf('blob:') === 0) return;

    var placeholder = this._placeholderSrc;
    if (src.indexOf('blob:') !== 0
        && src !== placeholder
        && (src.indexOf('http') === 0 || src === '')) {
      img.setAttribute('src', placeholder);
    }

    img.classList.add('intranet-pending-media');
    img.dataset.intranetHydrated = 'false';
  },

  _clearDriveImagePending: function (img) {
    if (!img) return;
    img.classList.remove('intranet-pending-media');
    img.dataset.intranetHydrated = 'true';
  },

  _prepareExternalImages: function (container) {
    if (!container) return;

    var imgs = container.querySelectorAll('img[data-external-image="true"], img[data-external-src]');
    for (var i = 0; i < imgs.length; i++) {
      var img = imgs[i];
      var extSrc = img.getAttribute('data-external-src') || '';
      if (!extSrc) {
        var src = img.getAttribute('src') || '';
        if (/^https?:\/\//i.test(src) && !img.getAttribute('data-drive-file-id')) {
          extSrc = src;
          img.setAttribute('data-external-src', extSrc);
          img.setAttribute('data-external-image', 'true');
        }
      }
      var liveSrc = img.getAttribute('src') || '';
      if (extSrc && liveSrc.indexOf('blob:') !== 0) {
        img.setAttribute('src', extSrc);
      }
      if (!img.getAttribute('referrerpolicy')) {
        img.setAttribute('referrerpolicy', 'no-referrer');
      }
    }
  },

  _hydrateExternalImages: async function (container, accessToken, apiBase) {
    var base = apiBase.endsWith('/') ? apiBase : apiBase + '/';
    var imgs = container.querySelectorAll('img[data-external-image="true"], img[data-external-src]');

    for (var i = 0; i < imgs.length; i++) {
      var img = imgs[i];
      if (img.getAttribute('data-drive-file-id')) continue;

      var extSrc = img.getAttribute('data-external-src');
      if (!extSrc) continue;

      var src = img.getAttribute('src') || '';
      if (img.dataset.externalPreview === 'true' && src.indexOf('blob:') === 0) continue;
      if (img.dataset.externalHydrated === 'true' && src.indexOf('blob:') === 0) continue;

      try {
        var resp = await fetch(base + 'intranet/fetch-external-image', {
          method: 'POST',
          headers: {
            Authorization: 'Bearer ' + accessToken,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({ url: extSrc })
        });
        if (!resp.ok) {
          console.warn('intranetMedia: external fetch failed', extSrc, resp.status);
          continue;
        }

        var data = await resp.json();
        if (!data || !data.base64) continue;

        var mime = data.mimeType || 'image/png';
        var binary = atob(data.base64);
        var bytes = new Uint8Array(binary.length);
        for (var j = 0; j < binary.length; j++) bytes[j] = binary.charCodeAt(j);

        var blob = new Blob([bytes], { type: mime });
        var blobUrl = URL.createObjectURL(blob);
        this._blobUrls.set(img, blobUrl);
        img.src = blobUrl;
        img.dataset.externalHydrated = 'true';
      } catch (e) {
        console.warn('intranetMedia: external hydrate failed', extSrc, e);
      }
    }
  },

  hydrate: async function (containerOrId, accessToken, apiBaseUrl) {
    var container = this._resolveContainer(containerOrId);
    if (!container) return;

    this._prepareExternalImages(container);

    if (!accessToken || !apiBaseUrl) return;

    var base = apiBaseUrl.endsWith('/') ? apiBaseUrl : apiBaseUrl + '/';
    var imgs = container.querySelectorAll('img[data-intranet-media], img[data-drive-file-id], img[src*="drive.google.com"]');

    for (var i = 0; i < imgs.length; i++) {
      var img = imgs[i];
      // Self-healing guard: only treat an image as done when it is actually showing a fetched
      // blob. A Blazor re-render (e.g. the auth state settling and reloading the page) can
      // recreate the image element back on the 1x1 placeholder while leaving this flag set;
      // trusting the flag alone would strand the image on the placeholder forever.
      if (img.dataset.intranetHydrated === 'true'
          && (img.getAttribute('src') || '').indexOf('blob:') === 0) continue;

      var driveFileId = this._extractDriveFileId(img);
      if (!driveFileId) continue;

      img.setAttribute('data-intranet-media', 'true');
      this._markDriveImagePending(img);

      try {
        var url = base + 'intranet/documents/drive/' + encodeURIComponent(driveFileId) + '/media';
        var resp = await fetch(url, {
          headers: { Authorization: 'Bearer ' + accessToken }
        });
        if (!resp.ok) {
          console.warn('intranetMedia: fetch failed', driveFileId, resp.status);
          continue;
        }

        var blob = await resp.blob();
        var blobUrl = URL.createObjectURL(blob);
        this._blobUrls.set(img, blobUrl);
        img.src = blobUrl;
        this._clearDriveImagePending(img);
      } catch (e) {
        console.warn('intranetMedia.hydrate failed for', driveFileId, e);
      }
    }

    await this._hydrateExternalImages(container, accessToken, apiBaseUrl);
  },

  revokeIn: function (containerOrId) {
    var container = this._resolveContainer(containerOrId);
    if (!container) return;

    var imgs = container.querySelectorAll('img[data-intranet-hydrated="true"]');
    for (var i = 0; i < imgs.length; i++) {
      var img = imgs[i];
      var blobUrl = this._blobUrls.get(img);
      if (blobUrl) {
        URL.revokeObjectURL(blobUrl);
        this._blobUrls.delete(img);
      }
      img.dataset.intranetHydrated = 'false';
    }
  }
};