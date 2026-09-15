window.xtreamForgeDialog = {
    _escapeHandler: null,
    focusFirst(dialog) {
        this.#focus(dialog, false);
    },
    focusLast(dialog) {
        this.#focus(dialog, true);
    },
    registerEscapeHandler(dotNetHelper) {
        this.unregisterEscapeHandler();
        this._escapeHandler = (event) => {
            if (event.key !== 'Escape') {
                return;
            }

            event.preventDefault();
            dotNetHelper.invokeMethodAsync('HandleEscapeFromJs');
        };

        document.addEventListener('keydown', this._escapeHandler, true);
    },
    unregisterEscapeHandler() {
        if (!this._escapeHandler) {
            return;
        }

        document.removeEventListener('keydown', this._escapeHandler, true);
        this._escapeHandler = null;
    },
    #focus(dialog, reverse) {
        if (!dialog) {
            return;
        }

        const focusable = Array.from(dialog.querySelectorAll(
            'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(element => !element.hasAttribute('hidden') && element.offsetParent !== null);

        const target = reverse ? focusable[focusable.length - 1] : focusable[0];
        (target ?? dialog).focus();
    }
};
