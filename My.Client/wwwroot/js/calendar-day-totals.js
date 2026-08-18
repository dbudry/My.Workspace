// Pins hour totals onto Heron month day-numbers and week/day headers.
// map: { "2026-08-13": "8h" }
// Totals live in a .pp-day-total badge (not a cell ::after) so they are not
// clipped by Heron's overflow:hidden / "+N more" footer on the last row or Sunday.
window.calendarDayTotals = {
  _timer: null,

  apply: function (map) {
    map = map || {};
    this.applyNow(map);
    // Heron PositionMonthItems runs in the same turn and can rebuild the cell
    // footer. Re-stamp after that so Sunday / last-row totals survive.
    var self = this;
    if (this._timer) clearTimeout(this._timer);
    this._timer = setTimeout(function () { self.applyNow(map); }, 60);
  },

  applyNow: function (map) {
    this.clear();

    document.querySelectorAll(".mud-cal-month-cell [data-date]").forEach(function (el) {
      var key = el.getAttribute("data-date");
      var label = key && map[key];
      if (!label) return;
      var cell = el.closest(".mud-cal-month-cell");
      if (!cell) return;
      var title = cell.querySelector(".mud-cal-month-cell-title");
      if (!title) return;
      stampBadge(title, label);
    });

    var holderByDate = {};
    document.querySelectorAll(".mud-cal-week-cell-holder [data-date]").forEach(function (el) {
      var key = el.getAttribute("data-date");
      if (!key || holderByDate[key]) return;
      var holder = el.closest(".mud-cal-week-cell-holder");
      if (holder) holderByDate[key] = { holder: holder, key: key };
    });

    var holders = document.querySelectorAll(".mud-cal-week-cell-holder");
    var labelsByHolder = [];
    holders.forEach(function (holder) {
      var found = null;
      Object.keys(holderByDate).forEach(function (key) {
        if (holderByDate[key].holder === holder) found = map[key];
      });
      labelsByHolder.push(found || "");
    });

    // First header child is the empty time gutter — skip it so Sunday is the
    // last day header, not dropped when the gutter is counted as a day.
    var headers = document.querySelectorAll(
      ".mud-cal-week-header > div, .mud-cal-work-week-header > div, .mud-cal-day-header > div");
    var hi = 0;
    headers.forEach(function (header) {
      if (header.children.length === 0 && !String(header.textContent || "").trim()) return;
      if (hi >= labelsByHolder.length) return;
      var label = labelsByHolder[hi];
      hi++;
      if (label) stampBadge(header, label);
    });
  },

  clear: function () {
    document.querySelectorAll(".pp-day-total").forEach(function (el) { el.remove(); });
    document.querySelectorAll("[data-day-total]").forEach(function (el) {
      el.removeAttribute("data-day-total");
    });
  }
};

function stampBadge(host, label) {
  host.setAttribute("data-day-total", label);
  var badge = host.querySelector(":scope > .pp-day-total");
  if (!badge) {
    badge = document.createElement("span");
    badge.className = "pp-day-total";
    host.appendChild(badge);
  }
  badge.textContent = label;
}
