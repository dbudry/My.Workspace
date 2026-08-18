// Global ArrowLeft/ArrowRight shortcut: advances a paged MudTable to the next/previous
// page, or (on the Tasks page's Weekly/Project week views) steps to the next/previous
// week. Pure DOM click-through ΓÇö no .NET interop needed, so it works on every page that
// has one of these controls without each page having to wire it up itself.
(function () {
    function isTypingIntoField(target) {
        if (!target) return false;
        var tag = target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
        if (target.isContentEditable) return true;
        return false;
    }

    function isInsideExcludedRegion(target) {
        if (!target || !target.closest) return false;
        // Dialogs, date-picker calendars, and open dropdowns/menus already use arrow
        // keys for their own navigation (day-to-day, month-to-month, option-to-option) ΓÇö
        // don't hijack those.
        return !!target.closest('.mud-dialog, .mud-picker-content, .mud-popover, .mud-calendar, .mud-menu');
    }

    function firstEnabledVisible(selector) {
        var els = document.querySelectorAll(selector);
        for (var i = 0; i < els.length; i++) {
            var el = els[i];
            if (!el.disabled && el.offsetParent !== null) return el;
        }
        return null;
    }

    function handleKeydown(e) {
        if (e.ctrlKey || e.metaKey || e.altKey || e.shiftKey) return;
        if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
        if (isTypingIntoField(e.target) || isInsideExcludedRegion(e.target)) return;

        var forward = e.key === 'ArrowRight';

        // Week navigation (Tasks page) takes priority over a table pager on the off
        // chance both are ever present on the same page ΓÇö it's the more specific control.
        var weekBtn = firstEnabledVisible(forward
            ? 'button[aria-label="Next week"]'
            : 'button[aria-label="Previous week"]');
        if (weekBtn) {
            e.preventDefault();
            weekBtn.click();
            return;
        }

        var pagerBtn = firstEnabledVisible(forward
            ? '.mud-table-pagination-next-button'
            : '.mud-table-pagination-before-button');
        if (pagerBtn) {
            e.preventDefault();
            pagerBtn.click();
        }
    }

    document.addEventListener('keydown', handleKeydown);
})();
