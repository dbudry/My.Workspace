// Details: Enter accepts (submit form, or notify Blazor when there is no form).
// Shift+Enter is a line break. Do not attach this to other inputs.
window.taskDetailsField = {
  attach: function (root, netRef) {
    if (!root) return false;
    var ta = root.querySelector("textarea");
    if (!ta) return false;
    if (ta.dataset.taskDetailsBound === "1") return true;
    ta.dataset.taskDetailsBound = "1";
    ta.addEventListener("keydown", function (e) {
      if (e.key !== "Enter" || e.shiftKey) return;
      e.preventDefault();
      e.stopPropagation();
      var form = ta.closest("form");
      if (form) {
        if (typeof form.requestSubmit === "function") form.requestSubmit();
        else form.submit();
        return;
      }
      if (netRef) netRef.invokeMethodAsync("NotifyPlainEnter");
    });
    return true;
  }
};
