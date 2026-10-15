// Desktop integration, 2026-10-15. MPL-2.0, see LICENSE.md.
(function () {
    'use strict';
    function validate(state) {
        if (!state || typeof state !== 'object' || Array.isArray(state)) throw new Error('小黑屋存档无效');
        ['game', 'stores', 'features', 'character', 'config'].forEach(function (key) {
            if (state[key] != null && (typeof state[key] !== 'object' || Array.isArray(state[key]))) throw new Error('小黑屋字段无效：' + key);
        });
    }
    // Retain native locations and onArrival logic, with one visible panel rather
    // than a 700 px sliding strip. Resources remain below the selected panel.
    function showLocation(module) {
        $('.location').removeClass('nezhaActive'); module.panel.addClass('nezhaActive');
        // The native world lives beside #main and normally slides over it.
        // Hide the village navigation / warehouse while exploring, as upstream
        // does, so clicking a tab cannot abandon the temporary expedition.
        $('#main, #nezhaStores').toggle(module !== World);
    }
    var travel = Engine.travelTo;
    Engine.travelTo = function (module) {
        var fx = $.fx.off; $.fx.off = true;
        try { travel.call(Engine, module); } finally { $.fx.off = fx; }
        showLocation(module);
        $('#storesContainer').appendTo('#nezhaStores');
    };
    // Embark, returning home and death bypass Engine.travelTo. Sync visibility
    // on arrival too, before upstream measures the map and bag widths.
    [Room, Outside, Path, World, Ship, Fabricator].forEach(function (module) {
        var arrival = module.onArrival;
        module.onArrival = function () {
            showLocation(module);
            var result = arrival.apply(this, arguments);
            window.scrollTo(0, 0);
            return result;
        };
    });
    function centreMap() {
        var viewport = document.querySelector('#nezhaMapViewport');
        var player = document.querySelector('#map .landmark');
        if (!viewport || !player || !World.panel.is(':visible')) return;
        viewport.style.height = Math.max(70, Math.min(320, innerHeight - viewport.getBoundingClientRect().top - 12)) + 'px';
        var v = viewport.getBoundingClientRect(), p = player.getBoundingClientRect();
        // The player is not always the first landmark once fog has cleared.
        document.querySelectorAll('#map .landmark').forEach(function (mark) {
            if (mark.firstChild && mark.firstChild.nodeValue === '@') p = mark.getBoundingClientRect();
        });
        viewport.scrollLeft += p.left + p.width / 2 - v.left - viewport.clientWidth / 2;
        viewport.scrollTop += p.top + p.height / 2 - v.top - viewport.clientHeight / 2;
    }
    var drawMap = World.drawMap;
    World.drawMap = function () {
        var result = drawMap.apply(this, arguments);
        if (!document.querySelector('#nezhaMapViewport')) {
            $('#map').wrap('<div id="nezhaMapViewport" tabindex="0" aria-label="探索地图，可滚动查看"></div>');
            $('<p id="nezhaMapHelp">').text('方向键 / WASD 移动；@ 是你，A 是村庄。地图可滚动查看。').insertBefore('#nezhaMapViewport');
        }
        centreMap();
        return result;
    };
    window.addEventListener('resize', centreMap);
    var updateSupplies = World.updateSupplies;
    World.updateSupplies = function () {
        var result = updateSupplies.apply(this, arguments);
        centreMap();
        return result;
    };
    document.addEventListener('keydown', function (event) {
        // Arrow keys move the wanderer, rather than also scrolling the map or
        // the entire page after the native movement handler has run.
        if (Engine.activeModule === World && event.which >= 37 && event.which <= 40) event.preventDefault();
    });
    Engine.moveStoresView = function () {};
    // The desktop package is silent and never fetches audio or analytics.
    AudioEngine.loadAudioFile = function () {};
    AudioEngine.init = function () {};
    AudioEngine.isAudioContextRunning = function () { return true; };
    Engine.toggleVolume = function () {};
    Engine.resumeAudioContext = function () {};
    // A cross-promotion opens another website, which this offline host blocks.
    // Leave the ordinary story / combat pools intact.
    Events.Marketing = [];
    // Native import / restart reloads go through the host's atomic save first.
    // The source changes to engine.js call reloadFromNative at those two sites.
    NezhaDesktop.configure({
        id: 'adarkroom',
        help: '点击生火开始。操作、资源和事件按原作逐渐解锁。小窗使用纵向排版，往下滚动可查看资源与消息。收起 / 失焦暂停；取消窗口的“失焦暂停”可在打开期间挂机。正常退出后保存原作进度，探索 / 事件现场按原作加载规则处理。',
        resumeHelp: '从生火开始慢慢探索。进度已保留，点击继续游戏。',
        credit: 'A Dark Room · doublespeak games · MPL-2.0。离线适配：哪吒桌宠。源码与许可随程序提供；原作 github.com/doublespeakgames/adarkroom。',
        validate: validate,
        prepare: function (record) {
            if (record) localStorage.gameState = JSON.stringify(record.state);
            else localStorage.removeItem('gameState');
            localStorage.lang = 'zh_cn';
        },
        start: function () {
            var shade = document.createElement('div'); shade.id = 'nezhaEventShade'; shade.hidden = true; document.body.append(shade);
            function syncEvents() {
                var active = Events.activeEvent();
                document.querySelectorAll('.eventPanel').forEach(function (panel) {
                    if (panel.parentNode !== document.body) document.body.append(panel);
                    panel.hidden = !!active && panel !== active.eventPanel[0];
                    panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
                    var heading = panel.querySelector('.eventTitle');
                    if (heading) { heading.id = 'nezhaEventTitle'; panel.setAttribute('aria-labelledby', heading.id); }
                });
                shade.hidden = !document.querySelector('.eventPanel:not([hidden])');
            }
            new MutationObserver(syncEvents).observe(document.body, { childList: true, subtree: true });
            syncEvents();
            var stores = document.createElement('div'); stores.id = 'nezhaStores'; document.querySelector('#wrapper').append(stores);
            var messages = document.createElement('h3'); messages.textContent = '消息'; document.querySelector('#wrapper').append(messages);
            document.querySelector('#wrapper').append(document.querySelector('#notifications'));
            showLocation(Engine.activeModule); $('#storesContainer').appendTo(stores);
            // Native language navigation would leave the fixed desktop entry URL.
            $('.menu, .logo').remove();
        },
        capture: function () { return JSON.parse(JSON.stringify(State)); },
        empty: function () { return { version: Engine.VERSION }; },
        readNative: function () { var state = JSON.parse(localStorage.gameState || '{}'); validate(state); return state; },
        pause: function (paused) { if (paused && AudioEngine._audioContext) AudioEngine._audioContext.suspend(); }
    });
}());
