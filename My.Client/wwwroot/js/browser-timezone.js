// Used by UserSettingsService to seed TimeZone on first login from the browser.
window.getBrowserTimeZone = function () {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || '';
    } catch {
        return '';
    }
};