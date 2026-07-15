// App-bar intranet search: "/" opens the panel when not typing in a field.
window.headerSearch = (function () {
  let handler = null;
  let dotnetRef = null;

  function isTypingIntoField(target) {
    if (!target) return false;
    var tag = target.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
    if (target.isContentEditable) return true;
    return false;
  }

  function registerShortcut(ref) {
    unregisterShortcut();
    dotnetRef = ref;
    handler = function (e) {
      if (e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !isTypingIntoField(e.target)) {
        e.preventDefault();
        if (dotnetRef && dotnetRef.invokeMethodAsync) {
          dotnetRef.invokeMethodAsync('OpenFromShortcut').catch(function () { });
        }
      }
    };
    document.addEventListener('keydown', handler);
  }

  function unregisterShortcut() {
    if (handler) {
      document.removeEventListener('keydown', handler);
      handler = null;
    }
    dotnetRef = null;
  }

  return { registerShortcut: registerShortcut, unregisterShortcut: unregisterShortcut };
})();