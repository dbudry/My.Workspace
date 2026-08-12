// Infinite-scroll helper for ProjectAutocomplete list panel.
window.projectAutocomplete = {
  isNearBottom: function (el, thresholdPx) {
    if (!el) return false;
    var threshold = typeof thresholdPx === 'number' ? thresholdPx : 80;
    // Not scrollable yet (content shorter than viewport) → not "near bottom" for load.
    if (el.scrollHeight <= el.clientHeight + 1) return false;
    return el.scrollHeight - el.scrollTop - el.clientHeight <= threshold;
  }
};
