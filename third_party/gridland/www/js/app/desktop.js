/* Nezha desktop adapter. Save rule data, not DOM objects or timer callbacks. */
define(['jquery','app/engine','app/eventmanager','app/gameboard','app/world','app/gamestate','app/gamecontent','app/desktopworld','app/gameoptions'],
function($,Engine,E,Board,World,State,Content,WorldSession,Options) {
    var restoring=false, pending=null, ready=false, ended=false, deathPending=false, retrying=false;
    function post(note) { if(window.chrome && chrome.webview) chrome.webview.postMessage(note); }
    function failedResume(message) {
        post({kind:'resume-error',message:message});
        $('#desktopHelp').text('旧续局未读取，旧文件保留。可返回列表，或重新开始。');
        if(!$('#desktopRestart').length) $('<button id="desktopRestart">重新开始格子王国</button>').appendTo('body').on('click',function(){
            if(!window.confirm('重新开始会清空格子王国当前进度。确定继续吗？')) return;
            localStorage.removeItem('slot0');window.nezhaInitialSession=null;window.nezhaResumeBlocked=false;pending=null;
            $('#desktopRestart').remove();post({kind:'new-session'});Engine.init();
        });
    }
    function pause() { E.trigger('pause'); World.desktopFreezeEffects(true); $('body').addClass('paused'); }
    function resume() { if(window.nezhaHostPaused || restoring || Desktop.saving || deathPending) return; E.trigger('unpause'); World.desktopFreezeEffects(false); }
    function showDeath() {
        deathPending=true; ended=true; pause();
        var info=State.getSlotInfo(0), day=info && info.day || 1;
        $('#desktopDeathMessage').text('已保存的成长保留，将从第 '+day+' 天白天重试。');
        $('body').addClass('desktopDeath');
    }
    function retry() {
        if(!deathPending || retrying || Desktop.saving) return;
        retrying=true; $('#desktopDeath button').prop('disabled',true).text('正在准备…');
        // Reload the local page in a fresh realm; old board timers cannot affect the retry.
        localStorage.nezhaDeathContinue='true'; post({kind:'retry'});
    }
    function clean(value,depth) {
        if((depth||0)>18) throw new Error('续局数据层级过深');
        if(value && typeof value==='object') Object.keys(value).forEach(function(key){if(key==='__proto__'||key==='constructor'||key==='prototype') throw new Error('续局数据字段无效');clean(value[key],(depth||0)+1);});
        if(typeof value==='number' && (!isFinite(value)||Math.abs(value)>1e10)) throw new Error('续局数值无效');
    }
    function validate(s) {
        clean(s); if(!s || s.version!==1 || !s.board || !s.world || !s.progress) throw new Error('续局版本不兼容');
        var b=s.board,w=s.world,p=s.progress;
        var letters=Object.keys(Content.ResourceType).map(function(key){return Content.ResourceType[key].char;}).join('');
        if(b.options.rows!==8 || b.options.columns!==8 || typeof b.tiles!=='string' || !(new RegExp('^(['+letters+']{8}X){8}$')).test(b.tiles)) throw new Error('棋盘格式无效');
        if(b.effects && (typeof b.effects!=='string'||b.effects.length!==72)) throw new Error('棋盘法术格式无效');
        if(!Array.isArray(p.buildings)||p.buildings.length>100||!Array.isArray(p.stores)||p.stores.length>100||typeof p.level!=='number'||p.level%1||p.level<1||p.level>26||typeof p.health!=='number'||p.health<=0||p.health>State.maxHealth(p.level)) throw new Error('成长记录无效');
        p.buildings.forEach(function(b){var type=b.options&&b.options.type&&Content.getBuildingType(b.options.type.className);if(!type||!b.requiredResources||typeof b.built!=='boolean')throw new Error('建筑记录无效');Object.keys(b.requiredResources).forEach(function(key){var n=b.requiredResources[key];if(typeof n!=='number'||n%1||n<0||n>type.cost[key])throw new Error('建筑资源无效');});});
        p.stores.forEach(function(block){if(!block.options||!block.options.type||!Content.getResourceType(block.options.type.className)||typeof block._quantity!=='number'||block._quantity<0||block._quantity>30)throw new Error('仓库记录无效');});
        if(typeof w.night!=='boolean'||typeof w.hasCelestial!=='boolean'||typeof w.position!=='number'||w.position%1||w.position<0||w.position>=(w.night?15:20)||!w.dude||typeof w.dude._position!=='number'||Math.abs(w.dude._position)>2000||!Array.isArray(w.actors)||w.actors.length>160||!Array.isArray(w.effects)||w.effects.length>3) throw new Error('战斗记录无效');
        w.actors.forEach(function(a){if(!a.data||!a.data.options||typeof a.data._position!=='number'||(a.kind!=='monster'&&a.kind!=='loot')||(a.kind==='monster'&&!require('app/entity/monster/monsterfactory')._monsters[a.name])) throw new Error('怪物记录无效');});
        w.effects.forEach(function(e){if(!Content.StateEffects[e.name]||e.remaining<0||e.remaining>60000) throw new Error('状态效果无效');});
        return s;
    }
    function stable() { return Engine.desktopStable() && Board.desktopStable() && World.desktopStable(); }
    function settle() {
        return new Promise(function(resolve,reject){
            var start=Date.now();
            function check(){
                // A close/save during the death animation must let the world persist
                // its death count and retry flag before the control is disposed.
                var dying=State.health<=0 && !deathPending;
                if(!dying && (ended || stable())) resolve();
                else if(Date.now()-start>7500) reject(new Error('棋盘动画尚未结束，请稍候再保存'));
                else setTimeout(check,20);
            }
            Engine.desktopPause(false); E.trigger('afterUnpaused'); check();
        });
    }
    function capture() {
        if(deathPending || (ended && State.health>0)) return Promise.resolve('null');
        if(!Engine.isStarted()) return Promise.resolve('menu');
        var paused=Engine.paused; Desktop.saving=true; $('body').addClass('desktopSaving'); World.desktopFreezeEffects(true);
        return settle().then(function(){
            if(ended || State.health<=0) return 'null';
            State.save(); var world=WorldSession.capture(),progress=JSON.parse(localStorage.slot0);
            if(world.carrying) progress.stores.push(world.carrying);
            delete world.carrying;
            var snapshot={version:1,board:WorldSession.plain(Board.desktopCapture()),world:world,progress:progress,options:JSON.parse(localStorage.gameOptions||'{}')};
            validate(snapshot); return JSON.stringify(snapshot);
        }).finally(function(){Desktop.saving=false;$('body').removeClass('desktopSaving');Engine.desktopPause(deathPending||paused||!!window.nezhaHostPaused);World.desktopFreezeEffects(Engine.paused);});
    }
    var Desktop=window.NezhaDesktop={
        saving:false,
        waitingForRetry:function(){return deathPending;},
        died:function(){
            // The world has counted this death and persisted the original growth checkpoint.
            // Retain the retry screen across window close; never restore the defeated battle.
            localStorage.nezhaDeathRetry='true'; showDeath();
        },
        engineReady:function(){
            E.bind('pause',function(){World.desktopFreezeEffects(true);});
            E.bind('unpause',function(){if(!window.nezhaHostPaused && !deathPending) World.desktopFreezeEffects(false);});
            E.bind('dudeDeath',function(){ended=true;post({kind:'ended'});});
            E.bind('gameOver',function(){ended=true;post({kind:'ended'});});
            E.bind('phaseChange',function(){if(State.health>0 && !deathPending)ended=false;});
            if(deathPending) showDeath();
        },
        started:function(){
            var wasRetry=retrying;
            if(retrying){
                retrying=false; deathPending=false; localStorage.removeItem('nezhaDeathRetry');localStorage.removeItem('nezhaDeathContinue');
                $('body').removeClass('desktopDeath'); $('#desktopDeath button').prop('disabled',false).text('继续');
            }
            ended=false;
            if(pending){var s=pending;pending=null;restoring=true;Desktop.saving=true;settle().then(function(){Board.desktopRestore(s.board);WorldSession.restore(s.world);pause();}).catch(function(e){failedResume(e.message);pause();}).finally(function(){restoring=false;Desktop.saving=false;post({kind:'ready'});});}
            else {post({kind:'new-session'}); if(window.nezhaHostPaused) pause(); else if(wasRetry) resume();}
            if(wasRetry) post({kind:'ready'});
        },
        setHostPaused:function(value){window.nezhaHostPaused=value;if(value)pause();},
        save:function(id){capture().then(function(json){post({kind:'saved',id:id,json:json});}).catch(function(e){post({kind:'save-error',id:id,message:e.message});});},
        testStart:function(){localStorage.removeItem('slot0');$('.saveSlot:first .slotSide').trigger('click');},
        testResume:function(){window.nezhaHostPaused=false;resume();},
        testMove:function(){
            var columns=Board.desktopCapture().tiles.split('X').slice(0,8).map(function(c){return c.split('');});
            function match(m){for(var c=0;c<8;c++)for(var r=0;r<8;r++)if((c<6&&m[c][r]===m[c+1][r]&&m[c][r]===m[c+2][r])||(r<6&&m[c][r]===m[c][r+1]&&m[c][r]===m[c][r+2]))return true;return false;}
            for(var c=0;c<8;c++)for(var r=0;r<8;r++)for(var d=0;d<2;d++){
                var c2=c+(d===0?1:0),r2=r+(d===1?1:0);if(c2>=8||r2>=8||columns[c][r]===columns[c2][r2])continue;
                var m=columns.map(function(col){return col.slice();}),temp=m[c][r];m[c][r]=m[c2][r2];m[c2][r2]=temp;
                if(match(m)){Board.switchTiles({row:r,col:c},{row:r2,col:c2});return true;}
            } return false;
        },
        testPhase:function(){E.trigger('phaseChange',[!World.isNight()]);},
        testSnapshot:function(){return {board:Board.desktopCapture(),world:WorldSession.capture(),health:State.health};},
        validate:validate,
        capture:capture
    };
    return {init:function(){
        $('body').append('<div id="desktopPause"><button>继续游戏</button></div><div id="desktopDeath"><div role="dialog" aria-modal="true" aria-labelledby="desktopDeathTitle" aria-describedby="desktopDeathMessage"><h2 id="desktopDeathTitle">你倒下了</h2><p id="desktopDeathMessage"></p><p class="deathHint">夜晚记得补充护盾和武器，再迎战怪物。</p><button data-clickable="true">继续</button></div></div><div id="desktopHelp">点击相邻格子交换 · 白天建设，夜晚战斗</div>');
        $('#desktopPause button').on('click',function(e){e.stopPropagation();resume();});
        $('#desktopDeath').on('mousedown touchstart click',function(e){e.stopPropagation();});
        $('#desktopDeath button').on('click',function(e){e.stopPropagation();retry();});
        deathPending=localStorage.nezhaDeathRetry==='true';
        retrying=deathPending && localStorage.nezhaDeathContinue==='true';
        if(retrying) $('#desktopDeath button').prop('disabled',true).text('正在准备…');
        if(window.nezhaResumeBlocked) failedResume('存档未读取');
        if(window.nezhaInitialSession && !deathPending) {
            try {pending=validate(window.nezhaInitialSession);localStorage.slot0=JSON.stringify(pending.progress);localStorage.gameOptions=JSON.stringify(pending.options||{});}
            catch(e){failedResume(e.message);pending=null;}
        }
        Engine.init();
        var timer=setInterval(function(){if(Engine.desktopReady()){clearInterval(timer);ready=true;if(pending || retrying) Engine.desktopStart(0);else post({kind:'ready'});}},30);
        window.addEventListener('error',function(e){post({kind:'error',message:e.message});});
    }};
});
