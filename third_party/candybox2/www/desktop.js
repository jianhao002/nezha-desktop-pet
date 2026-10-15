// Desktop integration, 2026-10-15. GPL-3.0-or-later, see COPYING.txt.
(function () {
    'use strict';
    var game, initial, firstLoad = true;
    var load = Saving.load, postLoad = Game.prototype.postLoad;
    // Native centering moves content left of a narrow viewport. Scroll a local
    // stage instead, preserving ASCII columns and the quest's camera position.
    RenderLocation.prototype.setContentGap = function (gap) {
        $(this.locationString).css('left', '0');
        if (game && game.getWeAreQuesting()) {
            var stage = document.querySelector('#nezhaStage'), content = document.querySelector(this.locationString);
            var cell = document.querySelector('#statusBar').getBoundingClientRect().width / 100;
            stage.scrollLeft = Math.max(0, (content.scrollWidth - gap * cell - stage.clientWidth) / 2);
        }
    };
    RenderLocation.prototype.setScrolling = function (value, defaultScroll) {
        if (this.scrolling !== value) {
            this.scrolling = value;
            var stage = document.querySelector('#nezhaStage');
            stage.scrollTop = defaultScroll || 0;
            stage.scrollLeft = value ? Math.max(0, (stage.scrollWidth - stage.clientWidth) / 2) : 0;
        }
    };
    RenderLocation.prototype.getScroll = function () { return document.querySelector('#nezhaStage').scrollTop; };
    function types() { return { bools: Saving.getAllBools(), numbers: Saving.getAllNumbers(), strings: Saving.getAllStrings() }; }
    function validate(state, requireKnown) {
        if (!state || typeof state !== 'object' || Array.isArray(state)) throw new Error('糖果盒子存档无效');
        var known = types();
        Object.keys(known).forEach(function (kind) {
            if (!state[kind] || typeof state[kind] !== 'object' || Array.isArray(state[kind])) throw new Error('糖果盒子字段缺失：' + kind);
            Object.keys(state[kind]).forEach(function (key) {
                if (!/^[a-zA-Z0-9_]{1,100}$/.test(key) || ((game || requireKnown) && !Object.prototype.hasOwnProperty.call(known[kind], key))) throw new Error('糖果盒子未知字段：' + key);
                var value = state[kind][key], type = kind === 'bools' ? 'boolean' : kind === 'numbers' ? 'number' : 'string';
                if (typeof value !== type || (type === 'number' && !Number.isFinite(value))) throw new Error('糖果盒子字段类型无效：' + key);
            });
        });
    }
    Saving.load = function (target, kind, value) {
        if (firstLoad) {
            firstLoad = false;
            // Equipment / grid item fields are registered by Game's constructor.
            // Validate their names now, before applying any persisted values.
            if (initial) {
                try { validate(initial, true); }
                catch (e) { initial = null; NezhaDesktop.blockResume(e.message); }
            }
            if (initial) {
                Object.keys(initial).forEach(function (group) {
                    Object.keys(initial[group]).forEach(function (key) {
                        var fn = group === 'bools' ? Saving.saveBool : group === 'numbers' ? Saving.saveNumber : Saving.saveString;
                        fn(key, initial[group][key]);
                    });
                });
            } else Saving.saveString('gameLanguage', 'zh');
        }
        load(target, kind, value);
    };
    Game.prototype.postLoad = function () { game = this; postLoad.call(this); };
    NezhaDesktop.configure({
        id: 'candybox2',
        help: '先等糖果增加，尝试吃糖和解锁新功能。保留原作 ASCII 图画与中文悬浮提示；地图较宽时左右滚动。收起 / 失焦暂停；取消窗口的“失焦暂停”可在打开期间挂机。关闭后保留资源、装备与解锁进度，重开回到糖果页面，当前战斗不逐帧续接。',
        resumeHelp: '糖果、装备和解锁进度已保留。重新打开从糖果页面继续。',
        credit: 'Candy Box 2 v1.2.3 · David “aniwey” L. · GPL-3.0-or-later。离线适配：哪吒桌宠。完整源码与许可随程序提供；原作 candybox2.github.io。',
        validate: validate,
        prepare: function (record) { initial = record ? record.state : null; },
        start: function () { if (!game) throw new Error('糖果盒子未能初始化'); },
        capture: function () { game.save(); game.getPlayer().save(); return JSON.parse(JSON.stringify(types())); },
        empty: function () { return { bools: {}, numbers: {}, strings: { gameLanguage: 'zh' } }; },
        pause: function () {}
    });
    // Engine access for verifying native production / eating in isolated QA data.
    window.NezhaCandy = function () { return game; };
}());
