window.dashboardLayout = {
    _drag: null,
    _bound: null,
    _specs: {},

    bind: function (gridEl, dotnetRef, specs) {
        if (specs)
            this._specs = specs;
        this._syncTiles(gridEl);
        if (this._bound && this._bound.gridEl === gridEl)
            return;
        this.unbind();
        if (!gridEl || !dotnetRef) return;

        var onDown = function (e) {
            var handle = e.target.closest ? e.target.closest('[data-dash-handle]') : null;
            if (!handle || !gridEl.contains(handle)) return;
            if (e.button != null && e.button !== 0) return;
            e.preventDefault();
            var tile = handle.closest('[data-dashboard-tile]');
            if (!tile) return;
            var id = tile.getAttribute('data-dashboard-tile');
            var spec = window.dashboardLayout._specFor(id, tile);
            var start = window.dashboardLayout._rectFromData(tile);
            window.dashboardLayout.startInteract(
                gridEl,
                id,
                handle.getAttribute('data-dash-handle'),
                start,
                spec,
                dotnetRef,
                e.pointerId,
                e.clientX,
                e.clientY);
        };

        this._bound = { gridEl: gridEl, onDown: onDown };
        gridEl.addEventListener('pointerdown', onDown);
    },

    unbind: function () {
        this.stopInteract();
        if (!this._bound) return;
        this._bound.gridEl.removeEventListener('pointerdown', this._bound.onDown);
        this._bound = null;
    },

    startInteract: function (gridEl, widgetId, mode, start, spec, dotnetRef, pointerId, clientX, clientY) {
        this.stopInteract();
        if (!gridEl || !widgetId || !dotnetRef) return;
        var tileEl = gridEl.querySelector('[data-dashboard-tile="' + widgetId + '"]');
        if (!tileEl) return;

        var metrics = this._metrics(gridEl);
        var grabCol = this._colAt(metrics, clientX) - start.col;
        var grabRow = this._rowAt(metrics, clientY) - start.row;
        var pending = {
            col: start.col,
            row: start.row,
            columns: start.columns,
            rows: start.rows
        };
        var finished = false;

        tileEl.classList.add(mode === 'move' ? 'dashboard-tile--dragging' : 'dashboard-tile--resizing');
        tileEl.style.zIndex = '5';
        this._applyRect(tileEl, pending, spec);
        try { tileEl.setPointerCapture(pointerId); } catch (err) { }

        var onMove = function (e) {
            var m = window.dashboardLayout._metrics(gridEl);
            var next = {
                col: pending.col,
                row: pending.row,
                columns: pending.columns,
                rows: pending.rows
            };
            if (mode === 'move') {
                next.col = window.dashboardLayout._clamp(
                    window.dashboardLayout._colAt(m, e.clientX) - grabCol,
                    1, 13 - start.columns);
                next.row = Math.max(1, window.dashboardLayout._rowAt(m, e.clientY) - grabRow);
            } else if (mode === 'east') {
                var cols = Math.round((e.clientX - tileEl.getBoundingClientRect().left + m.gapX) / (m.colW + m.gapX));
                next.columns = window.dashboardLayout._clamp(
                    cols, 1, Math.min(spec.maxColumns, 13 - start.col));
            } else if (mode === 'west') {
                var right = start.col + start.columns;
                var pointerCol = window.dashboardLayout._colAt(m, e.clientX);
                var maxCols = Math.min(spec.maxColumns, right - 1);
                next.columns = window.dashboardLayout._clamp(right - pointerCol, 1, maxCols);
                next.col = right - next.columns;
            } else if (mode === 'south') {
                var rows = Math.round((e.clientY - tileEl.getBoundingClientRect().top + m.gapY) / (m.rowH + m.gapY));
                next.rows = window.dashboardLayout._clamp(rows, 1, spec.maxRows);
            }
            var fitted = window.dashboardLayout._fit(spec, next);
            next.columns = fitted.columns;
            next.rows = fitted.rows;
            if (mode === 'west')
                next.col = (start.col + start.columns) - next.columns;
            next.col = window.dashboardLayout._clamp(next.col, 1, 13 - next.columns);
            if (next.col === pending.col && next.row === pending.row
                && next.columns === pending.columns && next.rows === pending.rows)
                return;
            pending = next;
            window.dashboardLayout._applyRect(tileEl, pending, spec);
        };

        var onUp = function () {
            if (finished) return;
            finished = true;
            window.dashboardLayout.stopInteract(pending);
            dotnetRef.invokeMethodAsync(
                'ApplyDashboardPlace',
                widgetId,
                mode,
                pending.col,
                pending.row,
                pending.columns,
                pending.rows);
        };

        this._drag = { onMove: onMove, onUp: onUp, tileEl: tileEl };
        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('pointercancel', onUp);
        onMove({ clientX: clientX, clientY: clientY });
    },

    stopInteract: function (keepRect) {
        var state = this._drag;
        if (!state) return;
        window.removeEventListener('pointermove', state.onMove);
        window.removeEventListener('pointerup', state.onUp);
        window.removeEventListener('pointercancel', state.onUp);
        if (state.tileEl) {
            state.tileEl.classList.remove('dashboard-tile--dragging', 'dashboard-tile--resizing');
            state.tileEl.style.zIndex = '';
            // Never blank gridColumn/gridRow. Blazor will not rewrite an unchanged
            // style attribute, so clearing here drops the tile out of the 12-col grid.
            var rect = keepRect || this._rectFromData(state.tileEl);
            this._applyRect(state.tileEl, rect, this._specFor(state.tileEl.getAttribute('data-dashboard-tile'), state.tileEl));
        }
        this._drag = null;
    },

    _syncTiles: function (gridEl) {
        if (!gridEl) return;
        var dragging = this._drag && this._drag.tileEl;
        var tiles = gridEl.querySelectorAll('[data-dashboard-tile]');
        for (var i = 0; i < tiles.length; i++) {
            if (tiles[i] === dragging) continue;
            this._applyRect(tiles[i], this._rectFromData(tiles[i]));
        }
    },

    _rectFromData: function (tileEl) {
        var spec = this._specFor(tileEl && tileEl.getAttribute('data-dashboard-tile'), tileEl);
        var rect = {
            col: this._num(tileEl && tileEl.getAttribute('data-col'), 1),
            row: this._num(tileEl && tileEl.getAttribute('data-row'), 1),
            columns: this._num(tileEl && tileEl.getAttribute('data-columns'), spec.minColumns),
            rows: this._num(tileEl && tileEl.getAttribute('data-rows'), spec.minRows)
        };
        var fitted = this._fit(spec, rect);
        rect.columns = fitted.columns;
        rect.rows = fitted.rows;
        return rect;
    },

    _fit: function (spec, rect) {
        var columns = rect.columns | 0;
        var rows = rect.rows | 0;
        if (spec.compactMinColumns && columns < spec.minColumns)
            rows = Math.max(rows, spec.compactMinRows);
        var minC = this._effectiveMinColumns(spec, rows);
        columns = this._clamp(columns, minC, spec.maxColumns);
        rows = this._clamp(rows, this._effectiveMinRows(spec, columns), spec.maxRows);
        columns = this._clamp(columns, this._effectiveMinColumns(spec, rows), spec.maxColumns);
        return { columns: columns, rows: rows };
    },

    _effectiveMinColumns: function (spec, rows) {
        if (spec.compactMinColumns && rows >= spec.compactMinRows)
            return spec.compactMinColumns;
        return spec.minColumns;
    },

    _effectiveMinRows: function (spec, columns) {
        if (spec.compactMinColumns && columns < spec.minColumns)
            return spec.compactMinRows;
        return spec.minRows;
    },

    _applyRect: function (tileEl, rect, spec) {
        if (!tileEl || !rect) return;
        spec = spec || this._specFor(tileEl.getAttribute('data-dashboard-tile'), tileEl);
        var fitted = this._fit(spec, rect);
        var columns = fitted.columns;
        var rows = fitted.rows;
        var col = this._clamp(rect.col, 1, 13 - columns);
        var row = Math.max(1, rect.row | 0);
        tileEl.style.gridColumn = col + ' / span ' + columns;
        tileEl.style.gridRow = row + ' / span ' + rows;
        this._setMetrics(tileEl, { col: col, row: row, columns: columns, rows: rows }, spec);
    },

    _setMetrics: function (tileEl, rect, spec) {
        var live = tileEl.querySelector('[data-dash-metrics-live]');
        if (live)
            live.textContent = rect.columns + ' \u00d7 ' + rect.rows;
        var colRange = tileEl.querySelector('[data-dash-metrics-cols]');
        var rowRange = tileEl.querySelector('[data-dash-metrics-rows]');
        if (spec && colRange)
            colRange.textContent = 'cols ' + this._effectiveMinColumns(spec, rect.rows) + '\u2013' + spec.maxColumns;
        if (spec && rowRange)
            rowRange.textContent = 'rows ' + this._effectiveMinRows(spec, rect.columns) + '\u2013' + spec.maxRows;
        var ghost = tileEl.querySelector('[data-dash-min-ghost]');
        if (ghost && spec) {
            ghost.style.width = ((this._effectiveMinColumns(spec, rect.rows) / rect.columns) * 100) + '%';
            ghost.style.height = ((this._effectiveMinRows(spec, rect.columns) / rect.rows) * 100) + '%';
        }
    },

    _metrics: function (gridEl) {
        var r = gridEl.getBoundingClientRect();
        var cs = getComputedStyle(gridEl);
        var gapX = parseFloat(cs.columnGap) || 0;
        var gapY = parseFloat(cs.rowGap) || 0;
        var colW = (r.width - gapX * 11) / 12;
        var rowH = parseFloat(cs.gridAutoRows);
        if (!rowH || isNaN(rowH)) rowH = 192;
        return { left: r.left, top: r.top, colW: colW, rowH: rowH, gapX: gapX, gapY: gapY };
    },

    _colAt: function (m, x) {
        return Math.floor((x - m.left) / (m.colW + m.gapX)) + 1;
    },

    _rowAt: function (m, y) {
        return Math.floor((y - m.top) / (m.rowH + m.gapY)) + 1;
    },

    _clamp: function (value, min, max) {
        if (typeof min !== 'number' || isNaN(min)) min = 1;
        if (typeof max !== 'number' || isNaN(max)) max = 12;
        if (max < min) max = min;
        if (typeof value !== 'number' || isNaN(value)) value = min;
        if (value < min) return min;
        if (value > max) return max;
        return value | 0;
    },

    _num: function (raw, fallback) {
        var n = parseInt(raw, 10);
        return isNaN(n) ? fallback : n;
    },

    _specFor: function (id, tile) {
        var fromNet = id && this._specs && (this._specs[id] || this._specs[id.charAt(0).toUpperCase() + id.slice(1)]);
        var minColumns = this._specNum(fromNet, 'minColumns', 'MinColumns', tile, 'data-min-columns', 4);
        var minRows = this._specNum(fromNet, 'minRows', 'MinRows', tile, 'data-min-rows', 1);
        var maxColumns = this._specNum(fromNet, 'maxColumns', 'MaxColumns', tile, 'data-max-columns', 12);
        var maxRows = this._specNum(fromNet, 'maxRows', 'MaxRows', tile, 'data-max-rows', 6);
        var compactMinColumns = this._specNum(fromNet, 'compactMinColumns', 'CompactMinColumns', tile, 'data-compact-min-columns', 0);
        var compactMinRows = this._specNum(fromNet, 'compactMinRows', 'CompactMinRows', tile, 'data-compact-min-rows', 0);
        var tileId = id || (tile && tile.getAttribute('data-dashboard-tile')) || '';
        if (tileId === 'topProjects' && compactMinColumns < 2) {
            compactMinColumns = 2;
            compactMinRows = Math.max(compactMinRows, 2);
        }
        return {
            minColumns: Math.max(1, minColumns),
            minRows: Math.max(1, minRows),
            maxColumns: Math.max(1, maxColumns),
            maxRows: Math.max(1, maxRows),
            compactMinColumns: Math.max(0, compactMinColumns),
            compactMinRows: Math.max(0, compactMinRows)
        };
    },

    _specNum: function (fromNet, camel, pascal, tile, attr, fallback) {
        if (fromNet) {
            if (fromNet[camel] != null && fromNet[camel] !== '')
                return this._num(fromNet[camel], fallback);
            if (fromNet[pascal] != null && fromNet[pascal] !== '')
                return this._num(fromNet[pascal], fallback);
        }
        return this._num(tile && tile.getAttribute(attr), fallback);
    }
};
