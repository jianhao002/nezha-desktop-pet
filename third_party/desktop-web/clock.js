// SPDX-License-Identifier: MIT
// Install before game scripts. Pausing retains each timer's remaining delay;
// restarting it does not count time spent hidden or fast-forward the game.
(function () {
    'use strict';
    var nativeTimeout = window.setTimeout.bind(window), nativeClear = window.clearTimeout.bind(window);
    var nativeRAF = window.requestAnimationFrame.bind(window), nativeCancelRAF = window.cancelAnimationFrame.bind(window);
    var NativeDate = window.Date, realNow = NativeDate.now.bind(NativeDate);
    var jobs = new Map(), frames = new Map(), next = 1, paused = false, stoppedAt = 0, skipped = 0;
    function now() { return (paused ? stoppedAt : realNow()) - skipped; }
    function GameDate() {
        var args = Array.prototype.slice.call(arguments);
        if (!(this instanceof GameDate)) return new NativeDate(now()).toString();
        return Reflect.construct(NativeDate, args.length ? args : [now()]);
    }
    GameDate.prototype = NativeDate.prototype;
    GameDate.now = now; GameDate.parse = NativeDate.parse; GameDate.UTC = NativeDate.UTC;
    window.Date = GameDate;
    function arm(job) {
        if (paused) return;
        job.due = realNow() + job.remaining;
        job.handle = nativeTimeout(function () {
            if (!jobs.has(job.id)) return;
            if (!job.repeat) jobs.delete(job.id);
            try { job.callback.apply(window, job.args); }
            finally { if (job.repeat && jobs.has(job.id)) { job.remaining = job.delay; arm(job); } }
        }, job.remaining);
    }
    function add(callback, delay, repeat, args) {
        if (typeof callback !== 'function') throw new TypeError('Only function timers are supported');
        delay = Math.max(repeat ? 1 : 0, Number(delay) || 0);
        var job = { id: next++, callback: callback, args: args, delay: delay, remaining: delay, repeat: repeat };
        jobs.set(job.id, job); arm(job); return job.id;
    }
    window.setTimeout = function (fn, delay) { return add(fn, delay, false, Array.prototype.slice.call(arguments, 2)); };
    window.setInterval = function (fn, delay) { return add(fn, delay, true, Array.prototype.slice.call(arguments, 2)); };
    window.clearTimeout = window.clearInterval = function (id) {
        var job = jobs.get(id); if (job) { nativeClear(job.handle); jobs.delete(id); }
    };
    function armFrame(frame) {
        if (paused) return;
        frame.handle = nativeRAF(function (time) { frames.delete(frame.id); frame.callback(time - skipped); });
    }
    window.requestAnimationFrame = function (fn) {
        var frame = { id: next++, callback: fn }; frames.set(frame.id, frame); armFrame(frame); return frame.id;
    };
    window.cancelAnimationFrame = function (id) {
        var frame = frames.get(id); if (frame) { nativeCancelRAF(frame.handle); frames.delete(id); }
    };
    window.NezhaClock = {
        setPaused: function (value) {
            if (paused === value) return;
            if (value) {
                stoppedAt = realNow(); paused = true;
                jobs.forEach(function (job) { job.remaining = Math.max(0, job.due - stoppedAt); nativeClear(job.handle); });
                frames.forEach(function (frame) { nativeCancelRAF(frame.handle); });
            } else {
                skipped += realNow() - stoppedAt; paused = false;
                jobs.forEach(arm); frames.forEach(armFrame);
            }
        },
        isPaused: function () { return paused; }
    };
}());
