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
    var travel = Engine.travelTo;
    Engine.travelTo = function (module) {
        var fx = $.fx.off; $.fx.off = true;
        try { travel.call(Engine, module); } finally { $.fx.off = fx; }
        $('.location').removeClass('nezhaActive'); module.panel.addClass('nezhaActive');
        $('#storesContainer').appendTo('#nezhaStores');
    };
    Engine.moveStoresView = function () {};
    // The desktop package is silent and never fetches audio or analytics.
    AudioEngine.loadAudioFile = function () {};
    AudioEngine.init = function () {};
    AudioEngine.isAudioContextRunning = function () { return true; };
    Engine.toggleVolume = function () {};
    Engine.resumeAudioContext = function () {};
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
            var stores = document.createElement('div'); stores.id = 'nezhaStores'; document.querySelector('#wrapper').append(stores);
            var messages = document.createElement('h3'); messages.textContent = '消息'; document.querySelector('#wrapper').append(messages);
            document.querySelector('#wrapper').append(document.querySelector('#notifications'));
            Engine.activeModule.panel.addClass('nezhaActive'); $('#storesContainer').appendTo(stores);
            // Native language navigation would leave the fixed desktop entry URL.
            $('.menu, .logo').remove();
        },
        capture: function () { return JSON.parse(JSON.stringify(State)); },
        empty: function () { return { version: Engine.VERSION }; },
        readNative: function () { var state = JSON.parse(localStorage.gameState || '{}'); validate(state); return state; },
        pause: function (paused) { if (paused && AudioEngine._audioContext) AudioEngine._audioContext.suspend(); }
    });
}());
