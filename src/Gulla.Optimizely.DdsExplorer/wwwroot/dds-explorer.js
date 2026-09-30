(function () {
    'use strict';

    var config = window.ddsExplorer || {};

    function $(id) { return document.getElementById(id); }

    function el(tag, attrs, children) {
        var node = document.createElement(tag);
        if (attrs) {
            for (var k in attrs) {
                if (k === 'text') node.textContent = attrs[k];
                else if (k === 'className') node.className = attrs[k];
                else if (k.indexOf('on') === 0) node.addEventListener(k.substring(2), attrs[k]);
                else node.setAttribute(k, attrs[k]);
            }
        }
        (children || []).forEach(function (c) {
            if (c != null) node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
        });
        return node;
    }

    function api(path, params) {
        var qs = new URLSearchParams(params || {}).toString();
        return config.api + path + (qs ? '?' + qs : '');
    }

    var sessionExpired = 'You appear to be signed out. Reload the page to sign in again.';

    function failure(status, message) {
        return { ok: false, status: status, data: { message: message } };
    }

    /**
     * Always resolves, never rejects: a network error or an unexpected answer comes back as
     * { ok: false } with a message, so every caller's !result.ok branch covers it.
     * Every mutating call carries the antiforgery token the page rendered; the API validates it.
     */
    function request(method, url, body) {
        // X-Requested-With makes cookie authentication answer an expired session with 401
        // instead of a redirect to the login page.
        var headers = { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
        if (method !== 'GET') headers[config.antiforgeryHeader] = config.antiforgeryToken;
        if (body !== undefined) headers['Content-Type'] = 'application/json';

        return fetch(url, {
            method: method,
            headers: headers,
            credentials: 'same-origin',
            // The API never redirects. A redirect is a sign-in challenge (OpenID Connect ignores
            // X-Requested-With); followed, it lands on a login page or fails CORS.
            redirect: 'manual',
            body: body === undefined ? undefined : JSON.stringify(body)
        }).then(function (response) {
            if (response.type === 'opaqueredirect' || response.status === 401) return failure(response.status, sessionExpired);
            if (response.status === 204) return { ok: true, status: 204, data: null };
            return response.text().then(function (text) {
                var data = null;
                // A non-JSON body is the site's own error page, not ours; its markup means nothing
                // to the editor, so it is dropped and errorMessage() falls back to the status.
                try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
                // Every successful answer other than 204 is JSON. Anything else was not written
                // by the API, and treating it as success would, for a save, report one that never happened.
                if (response.ok && data === null) return failure(response.status, 'The server gave an unexpected answer (HTTP ' + response.status + '). Reload the page and try again.');
                return { ok: response.ok, status: response.status, data: data };
            });
        }).catch(function (e) {
            return failure(0, 'The request did not reach the server (' + (e && e.message ? e.message : e) + ').');
        });
    }

    function errorMessage(result, fallback) {
        if (result && result.data) {
            if (result.data.message) return result.data.message;
            if (result.data.title) return result.data.title;
        }
        return fallback + (result ? ' (HTTP ' + result.status + ')' : '') + '. The site log may have the details.';
    }

    function formatNumber(n) {
        return n == null ? '–' : Number(n).toLocaleString();
    }

    function storeUrl(name) {
        // Each segment is encoded on its own so a slash in a store name survives the catch-all route.
        return config.root + '/store/' + name.split('/').map(encodeURIComponent).join('/');
    }

    // ---- Confirm dialog -------------------------------------------------------------------

    /**
     * Resolves true when confirmed. With options.typedName the confirm button stays disabled
     * until that exact text is typed — used for emptying and deleting stores.
     */
    function confirmDialog(options) {
        var dialog = $('dds-confirm');
        var input = $('dds-confirm-input');
        var ok = $('dds-confirm-ok');
        var cancel = $('dds-confirm-cancel');

        $('dds-confirm-title').textContent = options.title;
        var message = $('dds-confirm-message');
        message.textContent = '';
        (options.messages || []).forEach(function (m) {
            message.appendChild(el('p', { className: m.warn ? 'dds-alert dds-alert--warning' : '', text: m.text }));
        });
        ok.textContent = options.confirmText || 'Delete';

        var typed = !!options.typedName;
        $('dds-confirm-typed-row').hidden = !typed;
        $('dds-confirm-expected').textContent = options.typedName || '';
        input.value = '';
        ok.disabled = typed;

        return new Promise(function (resolve) {
            function onInput() { ok.disabled = input.value !== options.typedName; }
            function finish(value) {
                input.removeEventListener('input', onInput);
                ok.removeEventListener('click', onOk);
                cancel.removeEventListener('click', onCancel);
                dialog.removeEventListener('cancel', onCancel);
                dialog.close();
                resolve(value);
            }
            function onOk() { if (!ok.disabled) finish(true); }
            function onCancel(e) { if (e) e.preventDefault(); finish(false); }

            input.addEventListener('input', onInput);
            ok.addEventListener('click', onOk);
            cancel.addEventListener('click', onCancel);
            dialog.addEventListener('cancel', onCancel);
            dialog.showModal();
            (typed ? input : cancel).focus();
        });
    }

    var systemWarning = {
        warn: true,
        text: 'This looks like a system store, owned by Optimizely. Changing it can break the site.'
    };

    // ---- Store list -----------------------------------------------------------------------

    function initStoreList() {
        var stores = [];
        var filter = $('dds-store-filter');

        function render() {
            var term = filter.value.trim().toLowerCase();
            var rows = $('dds-store-rows');
            var visible = stores.filter(function (s) { return !term || s.name.toLowerCase().indexOf(term) >= 0; });

            rows.textContent = '';
            $('dds-store-count').textContent = term
                ? '(' + visible.length + ' of ' + stores.length + ')'
                : '(' + stores.length + ')';

            if (visible.length === 0) {
                rows.appendChild(el('tr', { className: 'dds-table__empty' }, [
                    el('td', { colspan: '3', text: stores.length ? 'No store matches the filter.' : 'The Dynamic Data Store has no stores.' })
                ]));
                return;
            }

            visible.forEach(function (s) {
                rows.appendChild(el('tr', null, [
                    el('td', null, [
                        el('a', { href: storeUrl(s.name), text: s.name }),
                        s.isSystem ? el('span', { className: 'dds-badge dds-badge--system', text: 'System' }) : null,
                        s.broken ? el('span', { className: 'dds-badge dds-badge--broken', text: 'Broken', title: s.error || '' }) : null
                    ]),
                    el('td', { className: 'dds-table__num', title: s.error || '' }, [
                        s.itemCount == null && s.error ? el('span', { className: 'dds-error-text', text: 'error' }) : formatNumber(s.itemCount)
                    ]),
                    el('td', { className: 'dds-table__num', text: formatNumber(s.propertyCount) })
                ]));
            });
        }

        filter.addEventListener('input', render);

        request('GET', api('/stores')).then(function (result) {
            if (!result.ok) {
                $('dds-store-rows').textContent = '';
                $('dds-store-rows').appendChild(el('tr', { className: 'dds-table__empty' }, [
                    el('td', { colspan: '3', className: 'dds-error-text', text: errorMessage(result, 'Could not load the stores') })
                ]));
                return;
            }
            stores = result.data || [];
            render();
        });
    }

    // ---- Store page -----------------------------------------------------------------------

    function initStorePage() {
        var storeName = config.store;
        var state = { page: 1, total: 0, pageSize: 50, columns: [], selected: new Set(), itemCount: null, raw: false };

        // Raw mode: the definition does not load, so the store is read straight from the DDS
        // tables. Everything that writes through DDS is switched off; only Delete store remains,
        // which the server carries out by name.
        function enterRawMode(loadError) {
            state.raw = true;
            var note = $('dds-store-raw');
            note.textContent = '';
            note.appendChild(el('strong', { text: 'This store\'s definition cannot be loaded. ' }));
            note.appendChild(document.createTextNode(
                'Usually a property\'s type came from an assembly that is no longer installed — Search & Navigation (Find) stores on a CMS 13 site, for example. ' +
                'The store is shown read-only, straight from the database: types as stored text, collections and references as their raw rows. ' +
                'It can still be deleted.'));
            note.appendChild(el('div', { className: 'dds-alert__detail' }, [el('code', { text: loadError })]));
            note.hidden = false;

            var empty = $('dds-empty-store');
            empty.disabled = true;
            empty.title = 'Emptying needs the store definition, which cannot be loaded. Delete the store instead.';
            $('dds-delete-selected').hidden = true;
        }

        function showStoreError(message) {
            var box = $('dds-store-error');
            box.textContent = message;
            box.hidden = !message;
        }

        /** Resolves true when the store loaded, so its items can be loaded next. */
        function loadDefinition() {
            return request('GET', api('/store', { name: storeName })).then(function (result) {
                if (!result.ok) {
                    showStoreError(errorMessage(result, 'Could not load the store'));
                    // Nothing below applies to a store that did not load — or does not exist.
                    $('dds-empty-store').disabled = true;
                    $('dds-delete-store').disabled = true;
                    renderItemMessage('The items were not loaded.');
                    return false;
                }
                var store = result.data;
                state.pageSize = store.pageSize;
                state.itemCount = store.itemCount;
                if (store.raw) enterRawMode(store.loadError);
                if (store.error) showStoreError('Could not count the items: ' + store.error);

                var rows = $('dds-definition-rows');
                rows.textContent = '';
                store.properties.forEach(function (p) {
                    rows.appendChild(el('tr', null, [
                        el('td', null, [el('code', { text: p.name })]),
                        el('td', null, [el('code', { text: p.type })]),
                        el('td', { text: p.mapType }),
                        el('td', { text: p.editable ? 'Yes' : 'No' })
                    ]));
                });
                $('dds-definition-summary').textContent = '(' + store.properties.length + ' properties)';
                return true;
            });
        }

        function loadItems() {
            state.selected.clear();
            updateSelection();

            return request('GET', api('/items', { store: storeName, page: state.page })).then(function (result) {
                if (!result.ok) {
                    renderItemMessage(errorMessage(result, 'Could not load the items'), true);
                    return;
                }
                var data = result.data;
                state.total = data.total;
                state.columns = data.columns;

                // A delete can leave the current page past the end; step back rather than show nothing.
                if (data.items.length === 0 && data.total > 0 && state.page > 1) {
                    state.page = Math.max(1, Math.ceil(data.total / data.pageSize));
                    return loadItems();
                }

                renderItems(data);
            });
        }

        function renderItemMessage(text, isError) {
            $('dds-item-head').textContent = '';
            var rows = $('dds-item-rows');
            rows.textContent = '';
            rows.appendChild(el('tr', { className: 'dds-table__empty' }, [
                el('td', { className: isError ? 'dds-error-text' : '', text: text })
            ]));
            $('dds-pager').hidden = true;
        }

        function renderItems(data) {
            $('dds-item-total').textContent = '(' + formatNumber(data.total) + ')';

            if (data.items.length === 0) {
                renderItemMessage('This store has no items.');
                return;
            }

            var selectAll = el('input', { type: 'checkbox', id: 'dds-select-all', 'aria-label': 'Select all items on this page' });
            selectAll.addEventListener('change', function () {
                // Read once: toggle() re-syncs selectAll.checked after every row.
                var on = selectAll.checked;
                document.querySelectorAll('.dds-item-check').forEach(function (cb) {
                    cb.checked = on;
                    toggle(cb.value, on);
                });
            });

            var head = $('dds-item-head');
            head.textContent = '';
            head.appendChild(el('tr', null, [
                state.raw ? null : el('th', { className: 'dds-table__check' }, [selectAll]),
                el('th', { text: 'Id' })
            ].concat(data.columns.map(function (c) { return el('th', { text: c }); }))));

            var rows = $('dds-item-rows');
            rows.textContent = '';
            data.items.forEach(function (item) {
                var check = el('input', { type: 'checkbox', className: 'dds-item-check', value: item.id, 'aria-label': 'Select item ' + item.id });
                check.addEventListener('change', function () { toggle(item.id, check.checked); });

                var cells = [
                    state.raw ? null : el('td', { className: 'dds-table__check' }, [check]),
                    el('td', null, [el('a', {
                        href: '#', className: 'dds-item-link', text: item.id,
                        onclick: function (e) { e.preventDefault(); openItem(item.id); }
                    })])
                ].concat(data.columns.map(function (c) {
                    return el('td', { className: 'dds-table__value', text: item.values[c] });
                }));

                rows.appendChild(el('tr', null, cells));
            });

            var pages = Math.max(1, Math.ceil(data.total / data.pageSize));
            $('dds-pager').hidden = pages <= 1;
            $('dds-page-info').textContent = 'Page ' + data.page + ' of ' + pages;
            $('dds-page-prev').disabled = data.page <= 1;
            $('dds-page-next').disabled = data.page >= pages;
        }

        function toggle(id, on) {
            if (on) state.selected.add(id); else state.selected.delete(id);
            updateSelection();
        }

        function updateSelection() {
            var button = $('dds-delete-selected');
            button.disabled = state.selected.size === 0;
            button.textContent = state.selected.size ? 'Delete selected (' + state.selected.size + ')' : 'Delete selected';

            // Keep "select all" in step with the rows ticked one by one.
            var selectAll = $('dds-select-all');
            if (selectAll) {
                var checks = document.querySelectorAll('.dds-item-check');
                var ticked = Array.prototype.filter.call(checks, function (cb) { return cb.checked; }).length;
                selectAll.checked = checks.length > 0 && ticked === checks.length;
                selectAll.indeterminate = ticked > 0 && ticked < checks.length;
            }
        }

        $('dds-page-prev').addEventListener('click', function () { state.page--; loadItems(); });
        $('dds-page-next').addEventListener('click', function () { state.page++; loadItems(); });

        $('dds-delete-selected').addEventListener('click', function () {
            var ids = Array.from(state.selected);
            confirmDialog({
                title: 'Delete ' + ids.length + ' item' + (ids.length === 1 ? '' : 's') + '?',
                messages: [{ text: 'The selected items are deleted from "' + storeName + '". This cannot be undone.' }]
                    .concat(config.isSystemStore ? [systemWarning] : [])
            }).then(function (ok) {
                if (!ok) return;
                request('POST', api('/items/delete', { store: storeName }), { ids: ids }).then(function (result) {
                    if (!result.ok) { showStoreError(errorMessage(result, 'Could not delete the items')); return; }
                    showStoreError('');
                    loadItems();
                });
            });
        });

        function storeMessages(action) {
            var count = state.total || state.itemCount;
            var messages = [{ text: action + ' This cannot be undone.' + (count ? ' The store holds ' + formatNumber(count) + ' items.' : '') }];
            if (config.isSystemStore) messages.push(systemWarning);
            return messages;
        }

        $('dds-empty-store').addEventListener('click', function () {
            confirmDialog({
                title: 'Empty store?',
                messages: storeMessages('Every item in "' + storeName + '" is deleted. The store and its definition are kept.'),
                typedName: storeName,
                confirmText: 'Empty store'
            }).then(function (ok) {
                if (!ok) return;
                request('POST', api('/store/empty', { name: storeName }), { confirmName: storeName }).then(function (result) {
                    if (!result.ok) { showStoreError(errorMessage(result, 'Could not empty the store')); return; }
                    showStoreError('');
                    state.page = 1;
                    loadItems();
                });
            });
        });

        $('dds-delete-store').addEventListener('click', function () {
            confirmDialog({
                title: 'Delete store?',
                messages: storeMessages('The store "' + storeName + '", its definition and every item in it are deleted.'),
                typedName: storeName,
                confirmText: 'Delete store'
            }).then(function (ok) {
                if (!ok) return;
                request('POST', api('/store/delete', { name: storeName }), { confirmName: storeName }).then(function (result) {
                    if (!result.ok) { showStoreError(errorMessage(result, 'Could not delete the store')); return; }
                    window.location.href = config.root;
                });
            });
        });

        // ---- Item dialog ----

        var item = null;

        function setEditing(editing) {
            $('dds-item-view').hidden = editing;
            $('dds-item-editor').hidden = !editing;
            $('dds-item-warning').hidden = !editing;
            $('dds-item-edit').hidden = editing;
            $('dds-item-cancel').hidden = !editing;
            $('dds-item-save').hidden = !editing;
            $('dds-item-delete').hidden = editing;
            if (editing) $('dds-item-editor').focus();
        }

        function showItemErrors(errors) {
            var box = $('dds-item-errors');
            box.textContent = '';
            box.hidden = !errors || errors.length === 0;
            if (box.hidden) return;

            box.appendChild(el('strong', { text: 'Nothing was saved.' }));
            box.appendChild(el('ul', null, errors.map(function (e) {
                return el('li', null, [e.field ? el('code', { text: e.field }) : null, e.field ? ': ' : null, e.message]);
            })));
        }

        function openItem(id) {
            request('GET', api('/item', { store: storeName, id: id })).then(function (result) {
                if (!result.ok) { showStoreError(errorMessage(result, 'Could not load the item')); return; }
                item = result.data;

                $('dds-item-id').textContent = item.id;
                $('dds-item-view').textContent = item.json;
                $('dds-item-editor').value = item.json;
                showItemErrors(null);

                var note = $('dds-item-readonly-note');
                var edit = $('dds-item-edit');
                edit.disabled = !item.canEdit;
                edit.title = item.canEdit ? '' : item.cannotEditReason;
                if (!item.canEdit) {
                    note.textContent = 'This item cannot be edited: ' + item.cannotEditReason;
                    note.hidden = false;
                } else if (item.readOnlyProperties.length) {
                    note.textContent = 'Read-only here (changes to them are rejected): ' + item.readOnlyProperties.join(', ') + '.';
                    note.hidden = false;
                } else {
                    note.hidden = true;
                }

                setEditing(false);
                // A raw item is database rows, not something DDS can load, edit or delete.
                edit.hidden = !!item.raw;
                $('dds-item-delete').hidden = !!item.raw;
                $('dds-item').showModal();
            });
        }

        /** False when there are unsaved edits and the user chooses to keep them. */
        function mayDiscardEdits() {
            var editor = $('dds-item-editor');
            return editor.hidden || !item || editor.value === item.json
                || window.confirm('Discard your unsaved changes?');
        }

        $('dds-item-close').addEventListener('click', function () {
            if (mayDiscardEdits()) $('dds-item').close();
        });

        $('dds-item-edit').addEventListener('click', function () {
            if (item && item.canEdit) setEditing(true);
        });

        $('dds-item-cancel').addEventListener('click', function () {
            $('dds-item-editor').value = item.json;
            showItemErrors(null);
            setEditing(false);
        });

        $('dds-item-save').addEventListener('click', function () {
            var save = $('dds-item-save');
            save.disabled = true;

            // The text goes to the server untouched; it is parsed there so no number is rounded.
            // The version lets the server refuse a save made from a copy that is no longer current.
            request('PUT', api('/item', { store: storeName, id: item.id }), { json: $('dds-item-editor').value, version: item.version })
                .then(function (result) {
                    save.disabled = false;
                    if (result.ok) {
                        $('dds-item').close();
                        loadItems();
                        return;
                    }
                    if (result.data && result.data.errors && result.data.errors.length) {
                        showItemErrors(result.data.errors);
                    } else {
                        showItemErrors([{ field: null, message: errorMessage(result, 'Could not save the item') }]);
                    }
                });
        });

        $('dds-item-delete').addEventListener('click', function () {
            var id = item.id;
            $('dds-item').close();
            confirmDialog({
                title: 'Delete item?',
                messages: [{ text: 'Item ' + id + ' is deleted from "' + storeName + '". This cannot be undone.' }]
                    .concat(config.isSystemStore ? [systemWarning] : [])
            }).then(function (ok) {
                if (!ok) return;
                request('DELETE', api('/item', { store: storeName, id: id })).then(function (result) {
                    if (!result.ok) { showStoreError(errorMessage(result, 'Could not delete the item')); return; }
                    loadItems();
                });
            });
        });

        // Closing the dialog while editing drops the unsaved text, so ask first — on Escape here,
        // and on the close button above.
        $('dds-item').addEventListener('cancel', function (e) {
            if (!mayDiscardEdits()) e.preventDefault();
        });

        loadDefinition().then(function (loaded) { if (loaded) loadItems(); });
    }

    if ($('dds-store-list-page')) initStoreList();
    if ($('dds-store-page')) initStorePage();
})();
