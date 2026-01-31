// localStorage helper for Blazor interop
// Key names must match C# LocalStorageKeys class in Frontend/LocalStorageKeys.cs
const Keys = Object.freeze({
    MailRecipient: 'mailRecipient',
    MailExcludedItems: 'mail_excluded_items',
    CdnRegion: 'cdn_region'
});

window.LocalStorageHelper = {
    Keys: Keys,
    setItem: function (key, value) {
        localStorage.setItem(key, value);
    },
    getItem: function (key) {
        return localStorage.getItem(key);
    },
    removeItem: function (key) {
        localStorage.removeItem(key);
    },
    getExcludedItems: function () {
        const data = localStorage.getItem(Keys.MailExcludedItems);
        return data ? JSON.parse(data) : [];
    },
    setExcludedItems: function (itemIds) {
        localStorage.setItem(Keys.MailExcludedItems, JSON.stringify(itemIds));
    },
    detectCdnRegion: function () {
        const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
        if (tz.startsWith('Europe/') || tz.startsWith('Africa/')) return 2; // EU
        if (tz === 'Asia/Seoul') return 3; // KR
        if (tz === 'Asia/Taipei') return 4; // TW
        return 1; // US (default)
    },
    getCdnRegion: function () {
        const stored = localStorage.getItem(Keys.CdnRegion);
        return stored ? parseInt(stored, 10) : 0; // 0 = Auto
    },
    setCdnRegion: function (region) {
        localStorage.setItem(Keys.CdnRegion, region.toString());
    }
};
