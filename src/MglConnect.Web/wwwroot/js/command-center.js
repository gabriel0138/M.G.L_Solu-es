(() => {
    const ready = () => {
        const root = document.documentElement;
        const themeButtons = [...document.querySelectorAll('[data-theme-toggle]')];
        const applyTheme = theme => {
            if (theme !== 'dark' && theme !== 'light') return;
            root.dataset.mglTheme = theme;
            root.style.colorScheme = theme;
            for (const button of themeButtons) {
                const label = theme === 'dark' ? 'Ativar modo claro' : 'Ativar modo escuro';
                button.setAttribute('aria-label', label);
                button.title = label;
                button.setAttribute('aria-pressed', String(theme === 'light'));
                const icon = button.querySelector('[data-theme-icon]');
                if (icon) icon.className = theme === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars';
            }
            document.querySelector('meta[name="theme-color"]')?.setAttribute('content',
                getComputedStyle(root).getPropertyValue('--mgl-bg-primary').trim());
        };
        applyTheme(root.dataset.mglTheme || 'dark');
        for (const button of themeButtons) button.addEventListener('click', () => {
            const theme = root.dataset.mglTheme === 'dark' ? 'light' : 'dark';
            applyTheme(theme);
            try { localStorage.setItem('mgl.ui.theme', theme); } catch { /* In-memory preference still works. */ }
        });
        window.addEventListener('storage', event => { if (event.key === 'mgl.ui.theme') applyTheme(event.newValue); });

        for (const button of document.querySelectorAll('[data-toggle-password]')) {
            const input = document.getElementById(button.dataset.togglePassword);
            if (!input) continue;
            button.addEventListener('click', () => {
                const reveal = input.type === 'password';
                input.type = reveal ? 'text' : 'password';
                button.setAttribute('aria-pressed', String(reveal));
                button.setAttribute('aria-label', reveal ? 'Ocultar senha' : 'Mostrar senha');
                const icon = button.querySelector('i');
                if (icon) icon.className = reveal ? 'bi bi-eye-slash' : 'bi bi-eye';
            });
        }

        for (const menu of document.querySelectorAll('.cc-account-menu')) {
            document.addEventListener('click', event => { if (!menu.contains(event.target)) menu.open = false; });
            menu.addEventListener('keydown', event => {
                if (event.key === 'Escape') { menu.open = false; menu.querySelector('summary')?.focus(); }
            });
        }

        const sidebar = document.getElementById('commandSidebar');
        const toggle = document.querySelector('[data-sidebar-toggle]');
        const backdrop = document.querySelector('.cc-sidebar-backdrop');
        const workspace = document.querySelector('.cc-workspace');
        const mobile = window.matchMedia('(max-width: 991.98px)');
        const focusable = element => [...element.querySelectorAll('a[href],button:not([disabled]),input:not([disabled]),[tabindex="0"]')]
            .filter(control => !control.hidden && control.getClientRects().length > 0);
        const setSidebar = (requested, restoreFocus = false) => {
            if (!sidebar || !toggle) return;
            const open = mobile.matches && requested;
            document.body.dataset.sidebarOpen = String(open);
            sidebar.inert = mobile.matches && !open;
            if (workspace) workspace.inert = open;
            sidebar.setAttribute('aria-hidden', String(mobile.matches && !open));
            toggle.setAttribute('aria-expanded', String(open));
            toggle.setAttribute('aria-label', open ? 'Fechar navegação' : 'Abrir navegação');
            if (backdrop) backdrop.hidden = !open;
            if (open) focusable(sidebar)[0]?.focus();
            else if (restoreFocus) toggle.focus();
        };
        if (sidebar && toggle) {
            setSidebar(false);
            toggle.addEventListener('click', () => setSidebar(document.body.dataset.sidebarOpen !== 'true'));
            for (const close of document.querySelectorAll('[data-sidebar-close]')) close.addEventListener('click', () => setSidebar(false, true));
            for (const link of sidebar.querySelectorAll('a[href]')) link.addEventListener('click', () => setSidebar(false));
            mobile.addEventListener('change', () => setSidebar(false));
            document.addEventListener('keydown', event => {
                if (!mobile.matches || document.body.dataset.sidebarOpen !== 'true') return;
                if (event.key === 'Escape') { event.preventDefault(); setSidebar(false, true); }
                if (event.key === 'Tab') {
                    const controls = focusable(sidebar);
                    const first = controls[0];
                    const last = controls[controls.length - 1];
                    if (event.shiftKey && (document.activeElement === first || !sidebar.contains(document.activeElement))) {
                        event.preventDefault(); last?.focus();
                    } else if (!event.shiftKey && (document.activeElement === last || !sidebar.contains(document.activeElement))) {
                        event.preventDefault(); first?.focus();
                    }
                }
            });
        }

        const palette = document.getElementById('commandPalette');
        const search = document.getElementById('commandSearch');
        const options = document.querySelector('[data-command-options]');
        const empty = document.querySelector('[data-command-empty]');
        if (palette && search && options && sidebar) {
            const normalize = text => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
            const destinations = [...sidebar.querySelectorAll('nav a[href]')].map(link => ({
                label: link.textContent.trim(), url: new URL(link.href, location.origin)
            })).filter(item => item.url.origin === location.origin);
            const render = () => {
                const query = normalize(search.value);
                const matches = destinations.filter(item => normalize(item.label).includes(query)).slice(0, 8);
                options.replaceChildren();
                for (const item of matches) {
                    const row = document.createElement('li');
                    const link = document.createElement('a');
                    link.href = item.url.pathname + item.url.search;
                    link.textContent = item.label;
                    row.append(link);
                    options.append(row);
                }
                if (empty) empty.hidden = matches.length > 0;
            };
            let opener;
            const open = trigger => {
                opener = trigger || document.activeElement;
                setSidebar(false);
                search.value = '';
                render();
                if (!palette.open) palette.showModal();
                search.focus();
            };
            for (const button of document.querySelectorAll('[data-command-open]')) button.addEventListener('click', () => open(button));
            for (const button of document.querySelectorAll('[data-command-close]')) button.addEventListener('click', () => palette.close());
            palette.addEventListener('close', () => {
                const visible = element => element?.isConnected && !element.closest('[inert]') && element.getClientRects().length > 0;
                const fallback = mobile.matches ? toggle : document.querySelector('[data-command-open]');
                (visible(opener) ? opener : fallback)?.focus();
            });
            palette.addEventListener('click', event => { if (event.target === palette) palette.close(); });
            search.addEventListener('input', render);
            search.addEventListener('keydown', event => {
                if (event.key === 'ArrowDown') { event.preventDefault(); options.querySelector('a')?.focus(); }
                if (event.key === 'Enter') { event.preventDefault(); options.querySelector('a')?.click(); }
            });
            document.addEventListener('keydown', event => {
                if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
                    event.preventDefault();
                    if (palette.open) palette.close(); else open();
                }
            });
        }
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready);
    else ready();
})();
