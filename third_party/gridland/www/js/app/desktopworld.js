/* Nezha desktop integration, 2026. Original Gridland modules remain MPL-2.0. */
define(['jquery','app/world','app/gamestate','app/gamecontent','app/graphics/graphics','app/eventmanager',
    'app/entity/dude','app/entity/celestial','app/entity/monster/monsterfactory','app/entity/loot/treasurechest','app/graphics/resources'],
function($,World,State,Content,G,E,Dude,Celestial,Monsters,Chest,ResourceGraphics) {
    var skip={action:1,carrying:1,target:1,held:1,stepFunction:1,_el:1,_width:1,_height:1,_head:1,_neck:1,_segments:1,_tail:1,_entity:1,__proto__:1,constructor:1,prototype:1};
    function plain(value,depth) {
        depth=depth||0; if(depth>12 || typeof value==='function') return undefined;
        if(value===null || typeof value!=='object') return value;
        if(value.jquery || value.nodeType) return undefined;
        var copy=Array.isArray(value)?[]:{};
        Object.keys(value).forEach(function(key){ if(!skip[key]) { var item=plain(value[key],depth+1); if(item!==undefined) copy[key]=item; } });
        return copy;
    }
    function assign(entity,data) {
        Object.keys(data).forEach(function(key){ if(!skip[key]) entity[key]=data[key]; });
        entity._el=null; entity.action=null; entity.tempAnimation=null; entity.frame=0; entity.stepFunction=null;
    }
    function capture() {
        var state=World.desktopState(), actors=[];
        state.stuff.forEach(function(entity){
            if(entity.hostile) actors.push({kind:'monster',name:entity.options.monsterClass,data:plain(entity)});
            else if(entity.lootable) actors.push({kind:'loot',data:plain(entity)});
        });
        var effects=[];
        Object.keys(state.effects).forEach(function(name){var record=state.effects[name];if(record) effects.push({name:name,remaining:Math.max(0,record.remaining-(record.started?Date.now()-record.started:0))});});
        return {night:state.isNight,position:state.position,hasCelestial:!!state.celestial,streak:state.streak||0,casualNight:!!state.casualNight,
            hasteTick:!!state.hasteTick,dude:plain(state.dude),actors:actors,effects:effects,carrying:plain(state.dude.carrying)};
    }
    function restore(state) {
        G.desktopResetAnimations(); $('.world').empty(); var stuff=[], dragon=null;
        State.buildings.forEach(function(building){
            building._el=null;
            if(!building.obsolete) { G.addToWorld(building); G.setPosition(building,building.p()); G.updateCosts(building); if(building.built) building.el().css('bottom','0px'); }
        });
        ResourceGraphics.desktopRestore();
        var dude=new Dude(); assign(dude,state.dude); dude.carrying=null; dude.paused=false; dude.makeIdle(); stuff.push(dude);
        G.addToWorld(dude); G.setPosition(dude,dude.p());
        var celestial=null;
        if(state.hasCelestial) {celestial=new Celestial(); celestial.animation(state.night?1:0);
            celestial.p(Math.floor(G.worldWidth()/(state.night?World.options.nightMoves:World.options.dayMoves))*state.position);
            stuff.push(celestial); G.addToWorld(celestial); G.moveCelestial(celestial);}
        G.setNight(state.night);
        state.actors.forEach(function(record){
            var entity=record.kind==='monster'?Monsters.getMonster(record.name,{target:dude}):new Chest();
            assign(entity,record.data); entity.target=dude; if(entity.options.monsterClass==='dragon') {entity.options.target=dude;dragon=entity;}
            stuff.push(entity); G.addToWorld(entity); G.setPosition(entity,entity.p());
            if(entity.hostile) { E.trigger('healthChanged',[entity]); if(entity.hp()<=0 && !entity.gone) entity.die(); }
        });
        World.desktopApply({stuff:stuff,dude:dude,celestial:celestial,isNight:state.night,position:state.position,
            streak:state.streak,casualNight:state.casualNight,hasteTick:state.hasteTick,dragon:dragon});
        G.updateHealth(State.health,State.maxHealth()); G.updateShield(dude.shield,State.maxShield()); G.updateSword(dude.sword,State.maxSword()); G.updateExperience(State.xp,dude.toLevel());
        E.trigger('updateMana',[State.mana,State.maxMana()]);
        if(State.prioritizedBuilding) {var building=State.buildings.filter(function(b){return b.options.type.className===State.prioritizedBuilding;})[0];if(building) E.trigger('prioritizeBuilding',[building]);}
        state.effects.forEach(function(effect){if(effect.remaining>0) World.desktopRestoreEffect(effect.name,effect.remaining);});
        World.desktopFreezeEffects(true);
    }
    return {capture:capture,restore:restore,plain:plain};
});
