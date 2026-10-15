// SPDX-License-Identifier: MIT
(function () {
    'use strict';
    var adapter, blocked = !!window.nezhaResumeBlocked, manual = true, host = !!window.nezhaHostPaused;
    var overlay, resume, title, detail, initialized = false;
    function post(note) { if (window.chrome && chrome.webview) chrome.webview.postMessage(note); }
    window.addEventListener('error', function (event) { if (event.message) post({ kind: 'error', message: event.message + ' / ' + event.filename + ':' + event.lineno }); });
    function check(value, depth) {
        if (depth > 26) throw new Error('存档层级过多');
        if (typeof value === 'number' && !Number.isFinite(value)) throw new Error('存档数值无效');
        if (value && typeof value === 'object') Object.keys(value).forEach(function (key) {
            if (key === '__proto__' || key === 'constructor' || key === 'prototype') throw new Error('存档字段无效');
            check(value[key], depth + 1);
        });
    }
    function validate(record) {
        if (!record || record.version !== 1 || record.game !== adapter.id) throw new Error('存档版本或游戏不匹配');
        if (JSON.stringify(record).length > 120000) throw new Error('存档过大');
        check(record, 0); adapter.validate(record.state); return record;
    }
    function refresh() {
        if (!initialized) return;
        var pause = blocked || manual || host;
        NezhaClock.setPaused(pause); adapter.pause(pause);
        document.body.classList.toggle('nezhaPaused', pause); overlay.hidden = !pause;
        title.textContent = blocked ? '存档暂未载入' : '已暂停';
        detail.textContent = blocked ? '旧存档已保留。可以返回游戏列表，或确认重新开始。' : adapter.resumeHelp;
        resume.disabled = blocked || host;
        resume.textContent = host ? '回到游戏窗口后继续' : '继续游戏';
    }
    function reload(record) {
        validate(record); manual = true; refresh(); post({ kind: 'reload', json: JSON.stringify(record) });
    }
    window.NezhaDesktop = {
        configure: function (game) {
            adapter = game;
            try { adapter.prepare(window.nezhaInitialSession == null ? null : validate(window.nezhaInitialSession)); }
            catch (e) { blocked = true; post({ kind: 'resume-error', message: e.message }); adapter.prepare(null); }
            $(function () {
                try {
                    adapter.start();
                    var bar = document.createElement('div'); bar.id = 'nezhaBar';
                    bar.innerHTML = '<button id="nezhaPauseButton">暂停</button><button id="nezhaNew">新游戏</button><details><summary>说明 / 来源</summary><p></p><p></p></details><small>离线 · 自动存档</small>';
                    bar.querySelectorAll('p')[0].textContent = adapter.help;
                    bar.querySelectorAll('p')[1].textContent = adapter.credit;
                    document.body.prepend(bar);
                    bar.querySelector('#nezhaPauseButton').onclick = function () { manual = true; refresh(); };
                    bar.querySelector('#nezhaNew').onclick = window.NezhaDesktop.newGame;
                    overlay = document.createElement('div'); overlay.id = 'nezhaPause';
                    overlay.innerHTML = '<section role="dialog" aria-label="游戏暂停"><h2></h2><p></p><button id="nezhaContinue">继续游戏</button><button id="nezhaReset">重新开始</button></section>';
                    document.body.append(overlay); title = overlay.querySelector('h2'); detail = overlay.querySelector('p'); resume = overlay.querySelector('#nezhaContinue');
                    resume.onclick = window.NezhaDesktop.resume;
                    overlay.querySelector('#nezhaReset').onclick = window.NezhaDesktop.newGame;
                    initialized = true; refresh(); post({ kind: 'ready' });
                } catch (e) { post({ kind: 'error', message: e.message }); }
            });
        },
        setHostPaused: function (value) { host = value; refresh(); },
        blockResume: function (message) { blocked = true; post({ kind: 'resume-error', message: message }); refresh(); },
        resume: function () { if (!blocked && !host) { manual = false; refresh(); } },
        newGame: function () {
            if (!confirm('确定重新开始？此游戏当前进度会被替换，其他游戏不受影响。')) return;
            reload({ version: 1, game: adapter.id, state: adapter.empty() });
        },
        reloadFromNative: function () { reload({ version: 1, game: adapter.id, state: adapter.readNative() }); },
        save: function (id) {
            if (blocked) { post({ kind: 'save-error', id: id, message: '旧存档尚未载入，请先返回或明确重新开始。' }); return; }
            try {
                var record = validate({ version: 1, game: adapter.id, state: adapter.capture() });
                post({ kind: 'saved', id: id, json: JSON.stringify(record) });
            } catch (e) { post({ kind: 'save-error', id: id, message: e.message }); }
        },
        snapshot: function () { return { version: 1, game: adapter.id, state: adapter.capture() }; },
        isPaused: function () { return NezhaClock.isPaused(); }
    };
    // Timers and mouse input pause together; keyboard shortcuts must also stop.
    ['keydown', 'keyup', 'keypress'].forEach(function (kind) {
        window.addEventListener(kind, function (event) {
            if (initialized && NezhaClock.isPaused() && !(event.target.tagName === 'BUTTON' && ['Tab', 'Enter', ' '].indexOf(event.key) >= 0)) event.stopImmediatePropagation();
        }, true);
    });
}());
