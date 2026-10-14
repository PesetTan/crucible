// Minimal interop shim for the existing GPL-3.0 Forge engine; the application is C#.
// SPDX-License-Identifier: GPL-3.0-or-later
import com.google.common.eventbus.Subscribe;
import com.google.gson.*;
import forge.*;
import forge.ai.*;
import forge.deck.Deck;
import forge.game.*;
import forge.game.ability.ApiType;
import forge.game.card.Card;
import forge.game.event.*;
import forge.game.player.*;
import forge.game.zone.ZoneType;
import forge.gui.GuiBase;
import forge.model.FModel;
import forge.game.spellability.Spell;
import forge.game.spellability.SpellAbility;
import forge.game.card.CardCollection;
import forge.game.card.CardCollectionView;
import forge.game.GameEntity;
import forge.game.cost.*;
import forge.game.spellability.TargetChoices;
import forge.game.player.DelayedReveal;
import forge.game.trigger.WrappedAbility;
import forge.util.MyRandom;
import forge.util.collect.FCollectionView;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

public final class ForgeBridge {
    static final String UNIFIED_REANIMATOR = "reanimator-v011";
    static final String PILOT = "reanimator-adaptive-v9";
    static final String CONTROL = "control-shared-v1";
    static final String AGGRO = "aggro-v001";
    static final Map<String,String> SUBTYPE_PARENTS = Map.ofEntries(
        Map.entry("auras-v001","forge-default"),
        Map.entry("lifegain-v001","forge-default"),
        Map.entry("artifacts-v001","forge-default"),
        Map.entry("skeletons-v001","forge-default"),
        Map.entry("jace-control-v001","control-shared-v1"),
        Map.entry("sacrifice-v001","forge-default"),
        Map.entry("dragons-v001","forge-default"),
        Map.entry("dwarves-v001","forge-default"),
        Map.entry("planeswalkers-v001","control-shared-v1"),
        Map.entry("discard-v001","forge-default"),
        Map.entry("landfall-v001","forge-default"),
        Map.entry("rabbits-v001","forge-default"),
        Map.entry("omniscience-v001","forge-default"),
        Map.entry("flash-v001","forge-default"),
        Map.entry("fling-v001","forge-default"),
        Map.entry("angels-v001","forge-default"),
        Map.entry("dinosaur-reanimator-v001","forge-default"),
        Map.entry("burn-v001","aggro-v001"),
        Map.entry("midrange-v001","forge-default"),
        Map.entry("tokens-v001","forge-default"),
        Map.entry("creature-aggro-v001","aggro-v001"),
        Map.entry("allies-v001","aggro-v001"),
        Map.entry("spell-control-v001","control-shared-v1"),
        Map.entry("spellementals-v001","forge-default"),
        Map.entry("jace-exile-v001","forge-default"),
        Map.entry("lessons-v001","control-shared-v1"),
        Map.entry("superfriends-v001","control-shared-v1"),
        Map.entry("demons-v001","forge-default"),
        Map.entry("elementals-v001","forge-default"),
        Map.entry("big-creatures-v001","forge-default")
    );
    static boolean usesControl(String profile){return CONTROL.equals(SUBTYPE_PARENTS.getOrDefault(profile,profile));}
    static boolean usesAggro(String profile){return AGGRO.equals(SUBTYPE_PARENTS.getOrDefault(profile,profile));}
    static final Gson json = new GsonBuilder().serializeNulls().disableHtmlEscaping().create();
    static PrintStream output;
    static String attemptId;
    static long sequence;
    static int actualTurn;
    static int turnLimit;
    static Game game;
    static Throwable captureFailure;
    static final Map<Card,String> speakerDiscardHints = new IdentityHashMap<>();
    static final Map<String,Map<Integer,PublicCard>> publiclySeen = new LinkedHashMap<>();
    static final Map<String,Integer> personalTurns = new LinkedHashMap<>();
    static final Map<String,Boolean> acceptedByPolicy = new LinkedHashMap<>();

    static Map<String,Object> map(Object... values) {
        Map<String,Object> result = new LinkedHashMap<>();
        for(int i=0;i<values.length;i+=2) result.put((String)values[i], values[i+1]);
        return result;
    }
    static void emit(Map<String,Object> value) {
        value.put("sequence", ++sequence); value.put("actualTurn", actualTurn); value.put("attemptId", attemptId);
        output.println(json.toJson(value)); output.flush();
    }
    static Object property(Object object,String method) {
        if(object==null)return null;
        try{return object.getClass().getMethod(method).invoke(object);}catch(Exception e){return null;}
    }
    static String str(Object value){return value==null?"":value.toString();}
    static final class OpponentEstimate {
        final String kind;
        final int confidence;
        final List<String> evidence;
        OpponentEstimate(String kind,int confidence,List<String> evidence){this.kind=kind;this.confidence=confidence;this.evidence=evidence;}
    }
    static final class PublicCard {
        final String name,oracle;
        final boolean creature,onBattlefield,exiled;
        final int manaValue;
        PublicCard(String name,String oracle,boolean creature,int manaValue,boolean onBattlefield,boolean exiled){
            this.name=name;this.oracle=oracle;this.creature=creature;this.manaValue=manaValue;this.onBattlefield=onBattlefield;this.exiled=exiled;
        }
    }
    // The classifier receives only public zones. Deck manifests and concealed zones never enter it.
    static OpponentEstimate estimateOpponent(Game g,Player self) {
        Map<Integer,PublicCard> visible=new LinkedHashMap<>();
        for(Player opponent:g.getRegisteredPlayers()) if(opponent!=self) {
            visible.putAll(publiclySeen.getOrDefault(opponent.getName(),Map.of()));
            for(ZoneType zone:new ZoneType[]{ZoneType.Battlefield,ZoneType.Graveyard,ZoneType.Exile})
                for(Card card:opponent.getCardsIn(zone)) {
                    if(card.isFaceDown())continue;
                    visible.put(card.getId(),new PublicCard(card.getName(),str(property(card.getCurrentState(),"getOracleText")),card.isCreature(),card.getCMC(),
                        zone==ZoneType.Battlefield,zone==ZoneType.Exile));
                }
        }
        return classifyPublicCards(new ArrayList<>(visible.values()));
    }
    static OpponentEstimate classifyPublicCards(List<PublicCard> visible) {
        int aggro=0,control=0,combo=0,creatures=0;
        List<String> evidence=new ArrayList<>();
        for(PublicCard card:visible) {
            String name=card.name;
            String oracle=card.oracle.toLowerCase(Locale.ROOT);
            if(card.creature&&card.onBattlefield)creatures++;
            if(card.creature&&card.manaValue<=2&&!card.exiled){aggro++;evidence.add(name+" (early creature)");}
            if(name.equals("Formidable Speaker")||name.equals("Superior Spider-Man")||name.equals("Bringer of the Last Gift")||name.equals("Overlord of the Balemurk")){
                combo+=3;evidence.add(name+" (revealed Reanimator card)");
            } else if(name.contains("Fling")||name.contains("Kona")) {combo+=2;evidence.add(name+" (revealed combo card)");}
            if(oracle.contains("counter target spell")||oracle.contains("destroy all creatures")||oracle.contains("exile all creatures")){
                control+=3;evidence.add(name+" (revealed control effect)");
            }
        }
        if(creatures>=2)aggro+=2;
        int best=Math.max(aggro,Math.max(control,combo));
        int second=aggro+control+combo-best-Math.min(aggro,Math.min(control,combo));
        String kind=best>=3 && best-second>=2 ? (best==combo?"combo":best==control?"control":"aggro") : "unknown";
        if(kind.equals("combo")&&evidence.stream().anyMatch(e->e.contains("Reanimator card")))kind="combo/reanimator";
        return new OpponentEstimate(kind,kind.equals("unknown")?0:Math.min(100,40+10*(best-second)),evidence.stream().distinct().limit(12).toList());
    }
    static void decision(String player,String profile,String action,List<String> options,String selected,OpponentEstimate estimate,String reason) {
        decision(player,profile,action,options,selected,estimate,reason,Map.of());
    }
    static void decision(String player,String profile,String action,List<String> options,String selected,OpponentEstimate estimate,String reason,Map<String,Object> context) {
        var record=map("kind","pilot_decision","player",player,"pilotProfile",profile,"pilotVersion",SUBTYPE_PARENTS.containsKey(profile)?profile:usesAggro(profile)?AGGRO:usesControl(profile)?"control-adaptive-v1":profile.equals(UNIFIED_REANIMATOR)?UNIFIED_REANIMATOR:profile.endsWith("-v10")?"reanimator-adaptive-v10":PILOT,"decision",action,
            "options",options,"selected",selected,"opponentClass",estimate.kind,"opponentConfidence",estimate.confidence,
            "visibleEvidence",estimate.evidence,"reason",reason);
        record.putAll(context);
        if(optimizerPoliciesByPlayer.containsKey(player))record.put("optimizerPolicyId",optimizerPoliciesByPlayer.get(player).get("id").getAsString());
        emit(record);
    }
    static final Map<String,Map<String,JsonObject>> attributesByPlayer=new LinkedHashMap<>();
    static final Map<String,JsonObject> optimizerPoliciesByPlayer=new LinkedHashMap<>();
    // Only diagnostic launchers set this; ordinary games receive policies in the request.
    static JsonObject optimizerFixturePolicy;
    static final Set<String> OPTIMIZER_PRIORITY_PATHS=Set.of("pilot.priority","pilotCopyPriority","pilotCastCopyPriority",
        "pilotDuplicateHandPriority","pilotDuplicatePriority","control.keepPriority","control.graveyardExilePriority");
    static boolean optimizerEnabled(Player player){return optimizerPoliciesByPlayer.containsKey(player.getName());}
    static void validateOptimizerPolicy(JsonObject policy,String profile) {
        if(!policy.has("version")||policy.get("version").getAsInt()!=1||!policy.has("id")
            ||!policy.get("id").getAsString().matches("[a-f0-9]{64}")||!policy.has("profile")
            ||!profile.equals(policy.get("profile").getAsString())||!policy.has("tuning")||!policy.get("tuning").isJsonObject())
            throw new IllegalArgumentException("Invalid optimizer policy identity");
        JsonObject tuning=policy.getAsJsonObject("tuning");
        if(tuning.size()>64)throw new IllegalArgumentException("Too many tuning entries");
        for(var entry:tuning.entrySet()) {
            String[] key=entry.getKey().split("\\|",-1);
            boolean path=key.length==2&&(OPTIMIZER_PRIORITY_PATHS.contains(key[1])
                ||key[1].equals("subtypePilot.developOnPass."+profile)&&SUBTYPE_PARENTS.containsKey(profile));
            if(!path||key[0].isEmpty()||!entry.getValue().isJsonPrimitive()||!entry.getValue().getAsJsonPrimitive().isNumber()
                ||!entry.getValue().getAsString().matches("-?[0-9]+")||entry.getValue().getAsInt() < -1000||entry.getValue().getAsInt()>1000)
                throw new IllegalArgumentException("Unregistered or out-of-bounds tuning entry: "+entry.getKey());
        }
    }
    static JsonObject tunedAttributes(JsonObject attrs,String name,JsonObject policy) {
        if(policy==null)return attrs;
        JsonObject result=null;
        for(var entry:policy.getAsJsonObject("tuning").entrySet()) {
            String prefix=name+"|";if(!entry.getKey().startsWith(prefix))continue;
            String[] path=entry.getKey().substring(prefix.length()).split("\\.");
            JsonObject current=attrs;boolean exists=true;
            for(int i=0;i<path.length-1;i++){if(!current.has(path[i])||!current.get(path[i]).isJsonObject()){exists=false;break;}current=current.getAsJsonObject(path[i]);}
            // Missing fixture/ability metadata stays inert; tuning cannot opt in a new shape.
            if(!exists||!current.has(path[path.length-1])||!current.get(path[path.length-1]).isJsonPrimitive()
                ||!current.get(path[path.length-1]).getAsJsonPrimitive().isNumber())continue;
            if(result==null)result=attrs.deepCopy();current=result;
            for(int i=0;i<path.length-1;i++)current=current.getAsJsonObject(path[i]);
            current.add(path[path.length-1],entry.getValue().deepCopy());
        }
        return result==null?attrs:result;
    }
    static JsonObject attributes(Player player,String name) {return tunedAttributes(attributesByPlayer.getOrDefault(player.getName(),Map.of()).getOrDefault(name,new JsonObject()),name,optimizerPoliciesByPlayer.get(player.getName()));}
    static JsonObject attributes(Card card) {return attributes(card.getOwner(),card.getName());}
    static boolean role(JsonObject attrs,String role) {
        if(!attrs.has("roles")||!attrs.get("roles").isJsonArray())return false;
        for(JsonElement item:attrs.getAsJsonArray("roles"))if(item.isJsonPrimitive()&&item.getAsJsonPrimitive().isString()&&item.getAsString().equals(role))return true;
        return false;
    }
    static boolean role(Player player,String name,String role) {return role(attributes(player,name),role);}
    static int number(JsonObject attrs,String key,int fallback) {
        if(!attrs.has(key)||!attrs.get(key).isJsonPrimitive()||!attrs.getAsJsonPrimitive(key).isNumber())return fallback;
        return attrs.get(key).getAsInt();
    }
    static JsonObject object(JsonObject attrs,String key) {return attrs.has(key)&&attrs.get(key).isJsonObject()?attrs.getAsJsonObject(key):new JsonObject();}
    // Own frozen annotations select a priority table, not a separate pilot identity.
    // Never read the opponent's deck, concealed cards, or library ordering.
    static String reanimatorScoreProfile(Player player,String profile) {
        if(!profile.equals(UNIFIED_REANIMATOR))return profile;
        Map<String,JsonObject> own=attributesByPlayer.getOrDefault(player.getName(),Map.of());
        for(String variant:List.of("cryomancer","sultai","terror"))
            for(JsonObject attrs:own.values()) {
                JsonElement value=object(attrs,"pilot").get("variant");
                if(value!=null&&value.isJsonPrimitive()&&value.getAsJsonPrimitive().isString()&&variant.equals(value.getAsString()))
                    return "reanimator-"+variant+"-v10";
            }
        // Compatibility fallback for older own-deck annotations, with historical precedence.
        if(own.containsKey("Seasoned Cryomancer"))return "reanimator-cryomancer-v10";
        if(own.containsKey("Jadzi, Steward of Fate")||own.containsKey("Oblivious Bookworm"))return "reanimator-sultai-v10";
        if(own.containsKey("Terror of the Peaks"))return "reanimator-terror-v10";
        return "reanimator-shared-v10";
    }
    static int attributeScore(JsonObject attrs,String profile,OpponentEstimate opponent,ZoneType destination,boolean hasCopier,boolean hasPayoff) {
        JsonObject pilot=object(attrs,"pilot");
        int score=number(pilot,"priority",0);
        String[] parts=profile.split("-");String variant=parts.length>1?parts[1]:"shared";
        score=number(object(pilot,"variantPriorities"),variant,score);
        score=number(object(attrs,"pilotDestinationPriorities"),destination.name().toLowerCase(Locale.ROOT),score);
        if(role(attrs,"graveyard_copier")&&destination==ZoneType.Hand&&hasCopier)score=number(attrs,"pilotDuplicateHandPriority",2);
        if(role(attrs,"mass_reanimation")&&hasPayoff)score=number(attrs,"pilotDuplicatePriority",5);
        String kind=opponent.kind.startsWith("combo")?"combo":opponent.kind;
        return score+number(object(pilot,"opponentBonuses"),kind,0);
    }
    static boolean blocksGraveyardSetup(JsonObject attrs,String oracle,boolean cloned) {
        JsonObject pilot=object(attrs,"pilot");
        if(!cloned&&pilot.has("blocksGraveyardSetup")&&pilot.get("blocksGraveyardSetup").isJsonPrimitive()&&pilot.getAsJsonPrimitive("blocksGraveyardSetup").isBoolean())return pilot.get("blocksGraveyardSetup").getAsBoolean();
        String text=oracle.toLowerCase(Locale.ROOT);return text.contains("graveyard")&&(text.contains("exile")||text.contains("shuffle")||text.contains("can't"));
    }
    static boolean preparedRecovery(boolean enabled,boolean recovery,boolean graveyardOrigin,long lands,boolean graveyardHate) {
        return enabled&&recovery&&graveyardOrigin&&lands>=4&&!graveyardHate;
    }
    static boolean enableLootDiscardSetup(boolean enabled,boolean heldCopier,boolean payoffInGraveyard,boolean graveyardHate) {
        return enabled&&heldCopier&&!payoffInGraveyard&&!graveyardHate;
    }
    static boolean enableTutorDiscardSetup(boolean enabled,boolean heldCopier,boolean heldSetupTarget,boolean payoffInGraveyard,boolean graveyardHate) {
        return enabled&&heldCopier&&heldSetupTarget&&!payoffInGraveyard&&!graveyardHate;
    }
    static int duplicateHandScore(int base,JsonObject attrs,ZoneType destination,boolean sameCardInHand) {
        return destination==ZoneType.Hand&&sameCardInHand?Math.min(base,number(attrs,"pilotDuplicateHandPriority",base)):base;
    }
    static int preparedComboScore(int base,boolean creatureTutor,boolean copier,ZoneType destination,boolean hasCopier,boolean payoffInGraveyard) {
        return creatureTutor&&copier&&destination==ZoneType.Hand&&!hasCopier&&payoffInGraveyard?150:base;
    }
    static int copyEffectScore(int base,JsonObject attrs,ApiType api,ZoneType destination,boolean sourceWasCast) {
        int score=api==ApiType.Clone&&destination==ZoneType.Battlefield?number(attrs,"pilotCopyPriority",base):base;
        return role(attrs,"mass_reanimation")&&attrs.has("requiresCastForEnterEffect")&&attrs.get("requiresCastForEnterEffect").isJsonPrimitive()&&attrs.getAsJsonPrimitive("requiresCastForEnterEffect").isBoolean()
            &&attrs.get("requiresCastForEnterEffect").getAsBoolean()&&api==ApiType.Clone&&destination==ZoneType.Battlefield&&sourceWasCast
            ?number(attrs,"pilotCastCopyPriority",100):score;
    }
    // Opt-in gates are checked against the offered card's actual entry triggers. Only the
    // copier's recorded payment is observed; printed mana costs do not establish payment.
    static int copyManaGateScore(int base,Card offered,SpellAbility choice) {
        JsonObject gate=object(attributes(offered),"pilotCopyManaGate");
        if(choice==null||choice.getApi()!=ApiType.Clone||offered.isCloned()||offered.isFaceDown()
            ||!gate.has("conditions")||!gate.get("conditions").isJsonArray())return base;
        JsonArray conditions=gate.getAsJsonArray("conditions");
        if(conditions.isEmpty()||conditions.size()>5)return base;
        boolean active=false;
        Card copier=choice.getHostCard();
        SpellAbility cast=copier==null?null:copier.getCastSA();
        for(JsonElement item:conditions) {
            if(!item.isJsonPrimitive()||!item.getAsJsonPrimitive().isString())return base;
            String condition=item.getAsString();
            if(!condition.matches("CastSA>Count\\$Adamant_2\\.(White|Blue|Black|Red|Green)\\.[1-5]\\.0"))return base;
            if(offered.getTriggers().stream().noneMatch(t->condition.equals(t.getParam("CheckSVar"))
                &&"Battlefield".equals(t.getParam("Destination"))&&"Card.Self".equals(t.getParam("ValidCard"))))return base;
            String[] parts=condition.split("\\.");byte color=forge.card.MagicColor.fromName(parts[1]);
            long paid=copier.wasCast()&&cast!=null?cast.getPayingMana().stream().filter(m->m.getColor()==color).count():0;
            if(paid>=Integer.parseInt(parts[2]))active=true;
        }
        return active?base:Math.min(base,number(gate,"inactivePriority",0));
    }
    static Map<String,Long> paidColors(Card source) {
        Map<String,Long> paid=new LinkedHashMap<>();SpellAbility cast=source.getCastSA();
        if(source.wasCast()&&cast!=null)for(byte color:forge.card.MagicColor.WUBRGC)
            paid.put(forge.card.MagicColor.toLongString(color),cast.getPayingMana().stream().filter(m->m.getColor()==color).count());
        return paid;
    }
    static boolean declineRedundantCopy(boolean optionalCopy,boolean hasChoices,boolean allRedundant) {
        return optionalCopy&&hasChoices&&allRedundant;
    }
    static boolean protectLastCopierDiscard(boolean hasPayoffRoute,long copiersInHand,boolean protectedCopier,int alternativeCount,int required) {
        return hasPayoffRoute&&copiersInHand>0&&copiersInHand<=required&&protectedCopier&&required>0&&alternativeCount>=required;
    }
    static boolean flag(JsonObject attrs,String key) {
        return attrs.has(key)&&attrs.get(key).isJsonPrimitive()&&attrs.getAsJsonPrimitive(key).isBoolean()&&attrs.get(key).getAsBoolean();
    }
    // Scores are bounded heuristics over own hand and visible battlefield, never outcomes.
    static boolean controlSweepImproves(int baselineNet,int candidateNet,int baselineKeep,int candidateKeep) {
        return candidateNet>0 && candidateNet>baselineNet && candidateNet-candidateKeep>baselineNet-baselineKeep;
    }
    static int controlKeepScore(JsonObject attrs,boolean land,long lands,long handLands,int landGoal) {
        return number(object(attrs,"control"),"keepPriority",0)+(land&&lands<landGoal&&handLands<=1?250:0);
    }
    static int controlGraveyardPriority(JsonObject attrs) {
        return number(object(attrs,"control"),"graveyardExilePriority",0);
    }
    // Only reviewed, simple opponent-only damage shapes may bypass the baseline's strategic pass.
    static boolean aggroBurnShape(SpellAbility sa) {
        if(sa==null||sa.getHostCard()==null||sa.getHostCard().isCloned()||!sa.isSpell()||sa.usesTargeting()
            ||sa.getSubAbility()!=null||sa.getPayCosts()==null||!sa.getPayCosts().isOnlyManaCost()
            ||!flag(object(attributes(sa.getHostCard()),"aggro"),"castOpponentBurnOnPass")
            ||!sa.hasParam("NumDmg")||!sa.getParam("NumDmg").matches("[1-9][0-9]?"))return false;
        return sa.getApi()==ApiType.DealDamage&&"Player.Opponent".equals(sa.getParam("Defined"))
            ||sa.getApi()==ApiType.DamageAll&&"Player.Opponent".equals(sa.getParam("ValidPlayers"))
                &&(!sa.hasParam("ValidCards")||"Creature.OppCtrl".equals(sa.getParam("ValidCards")));
    }
    static SpellAbility aggroBurnOnPass(Game g,Player p,List<SpellAbility> baseline) {return aggroBurnOnPass(g,p,baseline,AGGRO);}
    static SpellAbility aggroBurnOnPass(Game g,Player p,List<SpellAbility> baseline,String profile) {
        if(baseline!=null&&!baseline.isEmpty()||!g.getStack().isEmpty()||!g.getPhaseHandler().isPlayerTurn(p)
            ||!g.getPhaseHandler().getPhase().isMain())return null;
        for(Card card:p.getCardsIn(ZoneType.Hand))for(SpellAbility sa:card.getSpellAbilities()) {
            if(!aggroBurnShape(sa))continue;
            sa.setActivatingPlayer(p);
            if(!sa.canPlay()||!sa.canCastTiming(p)||!ComputerUtilCost.canPayCost(sa,p,false))continue;
            int damage=Integer.parseInt(sa.getParam("NumDmg"));
            boolean useful=false;
            for(Player opponent:p.getOpponents())if(opponent.canLoseLife()
                &&ComputerUtilCombat.predictDamageTo(opponent,damage,card,false)>0)useful=true;
            if(!useful)continue;
            decision(p.getName(),profile,"opponent-burn-on-pass",List.of("Forge pass",sa.getDescription()),sa.getDescription(),estimateOpponent(g,p),
                "Spend available mana on reviewed opponent-only damage after Forge declines further main-phase development.",
                map("source",card.getName(),"sourceId",card.getId(),"abilityApi",str(sa.getApi()),"damage",damage,"phase",str(g.getPhaseHandler().getPhase())));
            return sa;
        }
        return null;
    }
    // Bounded own-hand development. Printed metadata is inert for cloned/targeted/variable-cost shapes.
    static boolean subtypeDevelopmentShape(SpellAbility sa,String profile) {
        if(!SUBTYPE_PARENTS.containsKey(profile)||sa==null||sa.getHostCard()==null||!sa.isSpell()
            ||sa.getHostCard().isCloned()||sa.usesTargeting()||sa.getSubAbility()!=null
            ||sa.getPayCosts()==null||!sa.getPayCosts().isOnlyManaCost()
            ||sa.getHostCard().getManaCost().toString().contains("X")
            ||(sa.getApi()!=ApiType.PermanentCreature&&sa.getApi()!=ApiType.PermanentNoncreature))return false;
        return number(object(object(attributes(sa.getHostCard()),"subtypePilot"),"developOnPass"),profile,0)>0;
    }
    static SpellAbility subtypeDevelopmentOnPass(Game g,Player p,List<SpellAbility> baseline,String profile) {
        if(!SUBTYPE_PARENTS.containsKey(profile)||baseline!=null&&!baseline.isEmpty()||!g.getStack().isEmpty()
            ||!g.getPhaseHandler().isPlayerTurn(p)||!g.getPhaseHandler().getPhase().isMain()
            ||p.getCardsIn(ZoneType.Hand).size()>24)return null;
        SpellAbility best=null;int bestScore=0;List<Object> scores=new ArrayList<>();
        for(Card card:p.getCardsIn(ZoneType.Hand))for(SpellAbility sa:card.getSpellAbilities()) {
            if(!subtypeDevelopmentShape(sa,profile))continue;
            // Never replace a visible same-name legendary permanent merely to spend mana.
            if(card.getType().isLegendary()&&p.getCardsIn(ZoneType.Battlefield).stream().anyMatch(c->c.getName().equals(card.getName())))continue;
            sa.setActivatingPlayer(p);
            if(!sa.canPlay()||!sa.canCastTiming(p)||!ComputerUtilCost.canPayCost(sa,p,false))continue;
            int score=number(object(object(attributes(card),"subtypePilot"),"developOnPass"),profile,0);
            scores.add(map("id",card.getId(),"name",card.getName(),"score",score,"abilityApi",str(sa.getApi())));
            if(score>bestScore){best=sa;bestScore=score;}
        }
        if(best!=null)decision(p.getName(),profile,"subtype-development-on-pass",
            scores.stream().map(row->json.toJson(row)).toList(),best.getDescription(),estimateOpponent(g,p),
            "Develop the highest reviewed subtype permanent from own hand after Forge passes; engine timing, mana payment and resolution still apply.",
            map("source",best.getHostCard().getName(),"sourceId",best.getHostCard().getId(),"selectedId",best.getHostCard().getId(),
                "abilityApi",str(best.getApi()),"phase",str(g.getPhaseHandler().getPhase()),"baseline","Forge pass","candidateScores",scores));
        return best;
    }
    static boolean isReanimator(String profile){return profile.startsWith("reanimator-");}
    static int landRecoveryScore(int base,boolean isLand,ZoneType destination,long battlefieldLands,long handLands) {
        return isLand && destination==ZoneType.Hand && battlefieldLands<4 && handLands==0 ? 200 : base;
    }
    static boolean preserveBringerRecovery(boolean optional,ApiType api,ZoneType destination,boolean fromGraveyard,boolean hasSpider,long bringerCount,long lands) {
        return optional && api==ApiType.ChangeZone && destination==ZoneType.Hand && fromGraveyard && hasSpider && bringerCount==1 && lands<8;
    }
    static void hintBringerDiscard(Player p) {
        for (Card card : p.getCardsIn(ZoneType.Hand)) if (role(attributes(card),"discard_setup_target")) {
            if (!speakerDiscardHints.containsKey(card))
                speakerDiscardHints.put(card, card.hasSVar("DiscardMe") ? card.getSVar("DiscardMe") : null);
            card.setSVar("DiscardMe", "5");
        }
    }
    static void clearSpeakerDiscardHints() {
        for (var entry : speakerDiscardHints.entrySet()) {
            if (entry.getValue() == null) entry.getKey().removeSVar("DiscardMe");
            else entry.getKey().setSVar("DiscardMe", entry.getValue());
        }
        speakerDiscardHints.clear();
    }
    static String triggerCategory(String description) {
        String text=description.toLowerCase(Locale.ROOT).replace('’','\'');
        if(text.matches("(?s).*\\b(when|whenever)\\b.{0,100}\\b(enters|leaves|dies|put into (a |the )?graveyard|exiled)\\b.*"))return "Zone change";
        if(text.matches("(?s).*\\bat the beginning of\\b.*"))return "Turn or phase";
        if(text.matches("(?s).*\\b(when|whenever)\\b.{0,100}\\b(attacks|blocks|becomes blocked|deals combat damage)\\b.*"))return "Combat";
        if(text.matches("(?s).*\\b(when|whenever)\\b.{0,100}\\b(cast|casts|copy|copies)\\b.*"))return "Spell or ability";
        return "Other or unclassified";
    }
    static Map<String,Object> card(Card c) {
        return map("id",c.getId(),"name",c.getName(),"type",c.getType().toString(),"tapped",c.isTapped(),
            "power",c.isCreature()?c.getNetPower():null,"toughness",c.isCreature()?c.getNetToughness():null,
            "counters",str(property(c,"getCounters")),"attachedTo",c.getAttachedTo()==null?null:str(c.getAttachedTo().getId()),
            "owner",c.getOwner().getName(),"controller",c.getController().getName(),"cost",c.getManaCost().toString(),
            "text",str(property(c.getCurrentState(),"getOracleText")));
    }
    static List<Object> zone(Player p,ZoneType zone){List<Object> r=new ArrayList<>();for(Card c:p.getCardsIn(zone))r.add(card(c));return r;}
    static Map<String,Object> state() {
        List<Object> players=new ArrayList<>();
        for(Player p:game.getRegisteredPlayers())players.add(map("id",p.getName(),"life",p.getLife(),"handCount",p.getCardsIn(ZoneType.Hand).size(),
            "libraryCount",p.getCardsIn(ZoneType.Library).size(),"personalTurn",personalTurns.getOrDefault(p.getName(),0),
            "battlefield",zone(p,ZoneType.Battlefield),"graveyard",zone(p,ZoneType.Graveyard),"exile",zone(p,ZoneType.Exile),"hand",zone(p,ZoneType.Hand)));
        List<String> stack=new ArrayList<>();
        for(var item:game.getStack())stack.add(item.getStackDescription());
        var ph=game.getPhaseHandler();
        return map("activePlayer",ph.getPlayerTurn()==null?null:ph.getPlayerTurn().getName(),"phase",ph.getPhase()==null?null:ph.getPhase().name(),
            "engineTurn",ph.getTurn(),"players",players,"stack",stack);
    }
    static void checkpoint(String kind,String event,String detail,Boolean extraTurn) throws Exception {
        var s=state();String hash=HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(json.toJson(s).getBytes(StandardCharsets.UTF_8)));
        emit(map("kind",kind,"event",event,"detail",detail,"state",s,"stateHash",hash,"extraTurn",extraTurn));
    }
    public static final class Listener {
        @Subscribe public void event(GameEvent e) {
            try {
                String type=e.getClass().getSimpleName();
                if(e instanceof GameEventGameStarted started)emit(map("kind","game_started","starter",started.firstTurn().getName().equals("A")?0:1,"triggerCaptureVersion","1"));
                if(e instanceof GameEventTurnBegan) {
                    actualTurn++;
                    if(actualTurn==1)for(Player opening:game.getRegisteredPlayers())emit(map("kind","opening_hand","player",opening.getName(),
                        "mulligans",opening.getStats().getMulliganCount(),"keptHandSize",opening.getStats().getOpeningHandSize(),
                        "policyAccepted",acceptedByPolicy.getOrDefault(opening.getName(),false),"detail","Final kept hand under London mulligans; Forge Default keep policy."));
                    Player p=game.getPhaseHandler().getPlayerTurn();
                    personalTurns.merge(p.getName(),1,Integer::sum);
                    if(actualTurn>turnLimit){emit(map("kind","attempt_failed","reason","Administrative turn limit reached."));System.exit(4);}
                }
                if(e instanceof GameEventTurnEnded)clearSpeakerDiscardHints();
                if(e instanceof GameEventSpellAbilityCast cast && cast.si()!=null && !cast.si().isTrigger() && cast.sa()!=null && cast.sa().getHostCard()!=null) {
                    var view=cast.sa().getHostCard();
                    if(!view.isFaceDown()) {
                        Card source=null;
                        for(Card candidate:game.getCardsInGame())if(candidate.getId()==view.getId()){source=candidate;break;}
                        PublicCard fact=source==null?new PublicCard(view.getName(),"",false,99,false,false)
                            :new PublicCard(source.getName(),str(property(source.getCurrentState(),"getOracleText")),source.isCreature(),source.getCMC(),false,false);
                        publiclySeen.computeIfAbsent(cast.si().getActivatingPlayer().getName(),key->new LinkedHashMap<>()).put(view.getId(),fact);
                    }
                }
                if(type.equals("GameEventPlayerPriority")||type.equals("GameEventCardStatsChanged")||type.equals("GameEventZone")||type.equals("GameEventCombatUpdate"))return;
                if(e instanceof GameEventSpellAbilityCast cast && cast.si()!=null && cast.si().isTrigger()) {
                    String description=str(cast.sa()==null?null:cast.sa().getDescription());
                    emit(map("kind","trigger","event",type,"detail",e.toString(),"triggerCategory",triggerCategory(description),
                        "triggerText",description,"triggerSource",str(cast.si().getSourceCard())));
                }
                boolean capture=e instanceof GameEventTurnBegan || e instanceof GameEventTurnEnded || e instanceof GameEventTurnPhase ||
                    e instanceof GameEventGameStarted || e instanceof GameEventGameOutcome || e instanceof GameEventSpellAbilityCast ||
                    e instanceof GameEventSpellResolved || e instanceof GameEventPlayerLivesChanged || e instanceof GameEventCardChangeZone ||
                    e instanceof GameEventCardTapped || e instanceof GameEventCardCounters || e instanceof GameEventAttackersDeclared || e instanceof GameEventBlockersDeclared;
                if(capture)checkpoint("snapshot",type,e.toString(),e instanceof GameEventTurnBegan?game.getPhaseHandler().getPlayerTurn().isExtraTurn():null);
                else emit(map("kind","event","event",type,"detail",e.toString()));
                if ((e instanceof GameEventSpellResolved || type.equals("GameEventSpellRemovedFromStack"))
                        && e.toString().contains("When this creature enters") && e.toString().contains("Formidable Speaker"))
                    clearSpeakerDiscardHints();
            }catch(Throwable failure){captureFailure=failure;emit(map("kind","attempt_failed","reason","Replay capture failed: "+failure));System.exit(5);}
        }
    }
    public static final class ForcedLobby extends LobbyPlayerAi {
        private final int starter;
        private final String profile;
        public ForcedLobby(String name,int starter,String profile){super(name,null);this.starter=starter;this.profile=profile;setAiProfile("Default");
            if(optimizerFixturePolicy!=null){optimizerPoliciesByPlayer.remove(name);if(profile.equals(optimizerFixturePolicy.get("profile").getAsString())){validateOptimizerPolicy(optimizerFixturePolicy,profile);optimizerPoliciesByPlayer.put(name,optimizerFixturePolicy);}}
        }
        @Override public Player createIngamePlayer(Game g,int id){
            Player p=new Player(getName(),g,id);
            p.setFirstController(new PlayerControllerAi(g,p,this){
                private final Set<SpellAbility> declinedRecoveries=Collections.newSetFromMap(new IdentityHashMap<>());
                @Override public boolean confirmAction(SpellAbility sa,PlayerActionConfirmMode mode,String message,List<String> options,Card cardToShow,Map<String,Object> params) {
                    if(mode==PlayerActionConfirmMode.ChangeZoneGeneral && declinedRecoveries.remove(sa))return true;
                    return super.confirmAction(sa,mode,message,options,cardToShow,params);
                }
                private int sweepNet(SpellAbility sa,int damage) {
                    int score=0;
                    for(Player other:g.getRegisteredPlayers())for(Card creature:other.getCardsIn(ZoneType.Battlefield)) {
                        if(!creature.isCreature()||creature.isFaceDown()||!creature.canBeDestroyed()||creature.getShieldCount()>0)continue;
                        int predicted=ComputerUtilCombat.predictDamageTo(creature,damage,sa.getHostCard(),false);
                        if(predicted>0&&predicted>=creature.getLethalDamage())
                            score+=(other==p?-1:1)*ComputerUtilCard.evaluateCreature(creature);
                    }
                    return score;
                }
                private int keepScore(CardCollection cards,SpellAbility sa) {
                    int goal=number(object(attributes(sa.getHostCard()),"control"),"landGoal",5);
                    long lands=p.getCardsIn(ZoneType.Battlefield).stream().filter(Card::isLand).count();
                    long held=p.getCardsIn(ZoneType.Hand).stream().filter(Card::isLand).count();
                    int score=cards.stream().mapToInt(c->controlKeepScore(attributes(c),false,lands,held,goal)).sum();
                    if(held>0&&lands<goal&&cards.stream().filter(Card::isLand).count()>=held)score+=250;
                    return score;
                }
                @Override public CostDecisionMakerBase getCostDecisionMaker(Player payer,SpellAbility sa,boolean effect,String prompt) {
                    if(!usesControl(profile)||payer!=p||sa==null||sa.getHostCard()==null||sa.getHostCard().isCloned()
                        ||!flag(object(attributes(sa.getHostCard()),"control"),"discardMaxManaSweeper")||!effect
                        ||sa.getApi()!=ApiType.ImmediateTrigger||!"Discard<2/Card>".equals(sa.getParam("UnlessCost")))
                        return super.getCostDecisionMaker(payer,sa,effect,prompt);
                    return new AiCostDecision(payer,sa,effect) {
                        @Override public PaymentDecision visit(CostDiscard cost) {
                            PaymentDecision baseline=super.visit(cost);
                            if(baseline==null||baseline.cards==null||baseline.cards.size()!=2||!"Card".equals(cost.getType())
                                ||cost.getAbilityAmount(sa)!=2)return baseline;
                            CardCollection hand=new CardCollection(p.getCardsIn(ZoneType.Hand));
                            if(hand.size()>16||!hand.containsAll(baseline.cards))return baseline;
                            int baseDamage=baseline.cards.stream().mapToInt(Card::getCMC).max().orElse(0);
                            int baseNet=sweepNet(sa,baseDamage),baseKeep=keepScore(baseline.cards,sa);
                            CardCollection best=baseline.cards;int bestValue=baseNet-baseKeep,bestNet=baseNet;
                            List<Object> scores=new ArrayList<>();
                            for(int a=0;a<hand.size();a++)for(int b=a+1;b<hand.size();b++) {
                                CardCollection pair=new CardCollection();pair.add(hand.get(a));pair.add(hand.get(b));
                                int damage=Math.max(hand.get(a).getCMC(),hand.get(b).getCMC()),net=sweepNet(sa,damage),keep=keepScore(pair,sa);
                                scores.add(map("ids",pair.stream().map(Card::getId).toList(),"damage",damage,"netBattlefieldValue",net,"handCost",keep));
                                if(controlSweepImproves(baseNet,net,baseKeep,keep)&&net-keep>bestValue){best=pair;bestValue=net-keep;bestNet=net;}
                            }
                            decision(p.getName(),profile,"sweeper-discard",hand.stream().map(Card::getName).toList(),best.stream().map(Card::getName).toList().toString(),estimateOpponent(g,p),
                                best==baseline.cards?"Retain Forge discards; no bounded pair improves the visible sweeper trade.":"Discard an annotated pair that improves predicted enemy removal after own casualties and hand resource cost.",
                                map("source",sa.getHostCard().getName(),"abilityApi",str(sa.getApi()),"baselineIds",baseline.cards.stream().map(Card::getId).toList(),
                                    "selectedIds",best.stream().map(Card::getId).toList(),"baselineNet",baseNet,"selectedNet",bestNet,"candidateScores",scores));
                            return best==baseline.cards?baseline:PaymentDecision.card(best);
                        }
                    };
                }
                @Override public boolean payCostToPreventEffect(Cost cost,SpellAbility sa,boolean alreadyPaid,FCollectionView<Player> payers) {
                    if(!usesControl(profile)||sa==null||sa.getHostCard()==null||sa.getHostCard().isCloned()
                        ||!flag(object(attributes(sa.getHostCard()),"control"),"discardMaxManaSweeper")
                        ||sa.getApi()!=ApiType.ImmediateTrigger||!"Discard<2/Card>".equals(sa.getParam("UnlessCost")))
                        return super.payCostToPreventEffect(cost,sa,alreadyPaid,payers);
                    // Pinned Forge's implementation constructs AiCostDecision directly here.
                    // Keep its acceptance/payment gates, routing only the chosen cards through our visitor.
                    if(!SpellApiToAi.Converter.get(sa).willPayUnlessCost(p,sa,cost,alreadyPaid,payers)
                        ||!ComputerUtilCost.canPayCost(cost,sa,p,true))return false;
                    return new CostPayment(cost,sa).payComputerCosts(getCostDecisionMaker(p,sa,true,""));
                }
                private void controlTargets(SpellAbility sa) {
                    if(!usesControl(profile)||sa==null||sa.getHostCard()==null||sa.getHostCard().isCloned()
                        ||!flag(object(attributes(sa.getHostCard()),"control"),"prioritizeGraveyardExile")
                        ||sa.getApi()!=ApiType.ChangeZone||!"Graveyard".equals(sa.getParam("Origin"))||!"Exile".equals(sa.getParam("Destination"))
                        ||!sa.usesTargeting()||sa.getMinTargets()!=1||sa.getMaxTargets()!=1||sa.getTargets().size()!=1)return;
                    Card best=null;int priority=0;CardCollection offered=new CardCollection();
                    for(Player other:g.getRegisteredPlayers())if(other!=p)for(Card card:other.getCardsIn(ZoneType.Graveyard)) {
                        if(card.isFaceDown()||!sa.canTarget(card))continue;
                        offered.add(card);int score=controlGraveyardPriority(attributes(card));
                        if(score>priority){best=card;priority=score;}
                    }
                    Card old=sa.getTargets().getFirstTargetedCard();
                    if(best==null||old==best||old!=null&&old.getController()!=p&&controlGraveyardPriority(attributes(old))>=priority)return;
                    TargetChoices original=sa.getTargets(),chosen=new TargetChoices();chosen.add(best);sa.setTargets(chosen);
                    if(!sa.isTargetNumberValid()){sa.setTargets(original);return;}
                    decision(p.getName(),profile,"graveyard-exile-target",offered.stream().map(Card::getName).toList(),best.getName(),estimateOpponent(g,p),
                        "Exile the highest annotated public graveyard threat among engine-legal single targets.",
                        map("source",sa.getHostCard().getName(),"baselineTargetId",old==null?null:old.getId(),"selectedId",best.getId(),"priority",priority));
                }
                @Override public boolean chooseTargetsFor(SpellAbility sa) {
                    boolean result=super.chooseTargetsFor(sa);if(result)controlTargets(sa);return result;
                }
                @Override public List<SpellAbility> chooseSpellAbilityToPlay() {
                    List<SpellAbility> chosen=super.chooseSpellAbilityToPlay();
                    if(usesAggro(profile)) {
                        SpellAbility burn=aggroBurnOnPass(g,p,chosen,profile);
                        if(burn!=null)return List.of(burn);
                    }
                    SpellAbility development=subtypeDevelopmentOnPass(g,p,chosen,profile);
                    if(development!=null)return List.of(development);
                    if(usesControl(profile)&&chosen!=null)for(SpellAbility sa:chosen) {
                        controlTargets(sa);
                        decision(p.getName(),profile,"forge-action",List.of(sa.getDescription()),sa.getDescription(),estimateOpponent(g,p),
                            "Forge retains interaction timing, spell selection, mana payment, card advantage and win-condition sequencing.",
                            map("source",sa.getHostCard().getName(),"abilityApi",str(sa.getApi()),"targets",sa.getTargets().toString()));
                    }
                    return chosen;
                }
                private boolean speakerTutor(SpellAbility sa) {
                    if (!isReanimator(profile)) return false;
                    if (sa == null || sa.getHostCard() == null || !(role(p,sa.getHostCard().getName(),"creature_tutor") || role(p,sa.getHostCard().getName(),"graveyard_copier") && sa.hasParam("ChangeType") && sa.getParam("ChangeType").contains("Creature"))) return false;
                    if (!sa.hasParam("Origin") || !"Library".equals(sa.getParam("Origin")) || !"Hand".equals(sa.getParam("Destination"))) return false;
                    boolean inHand = p.getCardsIn(ZoneType.Hand).stream().anyMatch(c -> role(attributes(c),"graveyard_copier"));
                    boolean setup=enableTutorDiscardSetup(flag(object(attributes(sa.getHostCard()),"pilot"),"enableDiscardSetup"),inHand,
                        p.getCardsIn(ZoneType.Hand).stream().anyMatch(c->role(attributes(c),"discard_setup_target")),
                        p.getCardsIn(ZoneType.Graveyard).stream().anyMatch(c->role(attributes(c),"mass_reanimation")),opponentHasGraveyardHate());
                    return !inHand||setup;
                }
                private boolean opponentHasGraveyardHate() {
                    for (Player opponent : g.getRegisteredPlayers()) if (opponent != p) {
                        for (Card permanent : opponent.getCardsIn(ZoneType.Battlefield)) {
                            if(permanent.isFaceDown())continue;
                            String oracle = str(property(permanent.getCurrentState(), "getOracleText")).toLowerCase(Locale.ROOT);
                            if (blocksGraveyardSetup(attributes(permanent),oracle,permanent.isCloned()))
                                return true;
                        }
                    }
                    return false;
                }
                @Override public boolean confirmTrigger(WrappedAbility wrapper) {
                    if (speakerTutor(wrapper.getWrappedAbility()) && !p.getCardsIn(ZoneType.Hand).isEmpty()) {
                        boolean hate=opponentHasGraveyardHate();
                        if (!hate) hintBringerDiscard(p);
                        decision(p.getName(),profile,"speaker-trigger",List.of("accept","decline"),"accept",estimateOpponent(g,p),
                            hate?"Tutor accepted; visible graveyard interference prevents the Bringer discard hint.":"Tutor accepted; prefer discarding Bringer if it is in hand.");
                        return true;
                    }
                    return super.confirmTrigger(wrapper);
                }
                @Override public boolean playTrigger(Card host, WrappedAbility wrapper, boolean mandatory) {
                    SpellAbility triggered=wrapper.getWrappedAbility();
                    if(usesControl(profile)&&triggered!=null&&triggered.getHostCard()!=null
                        &&flag(object(attributes(triggered.getHostCard()),"control"),"prioritizeGraveyardExile")
                        &&triggered.getApi()==ApiType.ChangeZone&&!wrapper.hasParam("TargetingPlayer")&&!triggered.hasParam("TargetingPlayer")
                        &&"Graveyard".equals(triggered.getParam("Origin"))&&"Exile".equals(triggered.getParam("Destination"))) {
                        if(!getAi().doTrigger(wrapper,mandatory))return false;
                        controlTargets(triggered);
                        return ComputerUtil.playNoStack(wrapper.getActivatingPlayer(),wrapper,g,true);
                    }

                    if (speakerTutor(wrapper.getWrappedAbility()) && !p.getCardsIn(ZoneType.Hand).isEmpty()) {
                        // Forge's cost payer honors DiscardMe before its generic land/value heuristic.
                        boolean hate=opponentHasGraveyardHate();
                        if (!hate) hintBringerDiscard(p);
                        decision(p.getName(),profile,"speaker-trigger",List.of("accept","decline"),"accept",estimateOpponent(g,p),
                            hate?"Tutor accepted; visible graveyard interference prevents the Bringer discard hint.":"Tutor accepted; prefer discarding Bringer if it is in hand.");
                        return forge.ai.ComputerUtil.playNoStack(wrapper.getActivatingPlayer(), wrapper, g, true);
                    }
                    return super.playTrigger(host, wrapper, mandatory);
                }
                private Card policyCard(CardCollection candidates,ZoneType destination,SpellAbility sa,String action) {
                    if(!isReanimator(profile)||candidates.isEmpty()||sa==null||sa.getHostCard()==null)return null;
                    String source=sa.getHostCard().getName();
                    boolean relevant=role(p,source,"creature_tutor")||role(p,source,"graveyard_copier")||role(p,source,"hand_recovery")
                        ||role(p,source,"graveyard_reanimation")||role(p,source,"permanent_recovery")||role(p,source,"discard_outlet");
                    if(!relevant)return null;
                    boolean hasSpider=p.getCardsIn(ZoneType.Hand).stream().anyMatch(c->role(attributes(c),"graveyard_copier"));
                    boolean hasBringer=p.getCardsIn(ZoneType.Hand).stream().anyMatch(c->role(attributes(c),"mass_reanimation"))
                        ||p.getCardsIn(ZoneType.Graveyard).stream().anyMatch(c->role(attributes(c),"mass_reanimation"));
                    OpponentEstimate opponent=estimateOpponent(g,p);
                    Card best=null;int bestScore=0;List<Object> scores=new ArrayList<>();
                    for(Card card:candidates){
                        int score=attributeScore(attributes(card),reanimatorScoreProfile(p,profile),opponent,destination,hasSpider,hasBringer);
                        boolean complete=preparedRecovery(flag(object(attributes(sa.getHostCard()),"pilot"),"completePreparedCombo"),
                            sa.getApi()==ApiType.ChangeZone&&(role(p,source,"hand_recovery")||role(p,source,"permanent_recovery")||role(p,source,"graveyard_copier")),
                            sa.hasParam("Origin")&&sa.getParam("Origin").contains("Graveyard"),p.getCardsIn(ZoneType.Battlefield).stream().filter(Card::isLand).count(),opponentHasGraveyardHate());
                        score=preparedComboScore(score,speakerTutor(sa)||role(p,source,"creature_tutor")||complete,role(attributes(card),"graveyard_copier"),destination,hasSpider,
                            p.getCardsIn(ZoneType.Graveyard).stream().anyMatch(c->role(attributes(c),"mass_reanimation")));
                        if(role(p,source,"graveyard_copier")) {
                            score=copyEffectScore(score,attributes(card),sa.getApi(),destination,sa.getHostCard().wasCast());
                            if((profile.endsWith("-v10")||profile.equals(UNIFIED_REANIMATOR))&&destination==ZoneType.Battlefield)score=copyManaGateScore(score,card,sa);
                        }
                        if(role(p,source,"permanent_recovery") && sa.getApi()==ApiType.ChangeZone)
                            score=landRecoveryScore(score,card.isLand(),destination,
                                p.getCardsIn(ZoneType.Battlefield).stream().filter(Card::isLand).count(),p.getCardsIn(ZoneType.Hand).stream().filter(Card::isLand).count());
                        long held=p.getCardsIn(ZoneType.Hand).stream().filter(c->c.getName().equals(card.getName())).count();
                        score=duplicateHandScore(score,attributes(card),destination,held>0);
                        scores.add(map("id",card.getId(),"name",card.getName(),"score",score,"copiesInOwnHand",held));
                        if(score>bestScore){best=card;bestScore=score;}
                    }
                    if(best!=null)decision(p.getName(),profile,action,candidates.stream().map(Card::getName).toList(),best.getName(),opponent,
                        "Choose a legal "+source+" option using the shared Reanimator priorities and visible opponent evidence.",
                        map("priorityVariant",reanimatorScoreProfile(p,profile),"source",source,"abilityApi",str(sa.getApi()),"destination",str(destination),"sourceWasCast",sa.getHostCard().wasCast(),
                            "abilityDescription",sa.getDescription(),"candidateIds",candidates.stream().map(Card::getId).toList(),"selectedId",best.getId(),"candidateScores",scores,
                            "sourceId",sa.getHostCard().getId(),"sourcePaidColors",paidColors(sa.getHostCard())));
                    return best;
                }
                @Override public Card chooseSingleCardForZoneChange(ZoneType destination, List<ZoneType> origin, SpellAbility sa,
                        CardCollection fetchList, DelayedReveal delayedReveal, String prompt, boolean optional, Player decider) {
                    if(isReanimator(profile)&&decider==p&&(origin.contains(ZoneType.Graveyard)||origin.contains(ZoneType.Library)||sa!=null&&sa.getHostCard()!=null&&role(p,sa.getHostCard().getName(),"permanent_recovery"))) {
                        String source=sa!=null&&sa.getHostCard()!=null?sa.getHostCard().getName():"";
                        boolean recovery=role(p,source,"permanent_recovery")||role(p,source,"hand_recovery")||role(p,source,"graveyard_copier");
                        if(recovery && preserveBringerRecovery(optional,sa.getApi(),destination,origin.contains(ZoneType.Graveyard),
                                p.getCardsIn(ZoneType.Hand).stream().anyMatch(c->role(attributes(c),"graveyard_copier")),
                                p.getCardsIn(ZoneType.Graveyard).stream().filter(c->role(attributes(c),"mass_reanimation")).count(),
                                p.getCardsIn(ZoneType.Battlefield).stream().filter(Card::isLand).count())) {
                            CardCollection alternatives=new CardCollection();
                            for(Card candidate:fetchList)if(!(candidate.getOwner()==p && candidate.getZone()!=null && candidate.getZone().getZoneType()==ZoneType.Graveyard
                                    && role(attributes(candidate),"mass_reanimation")))alternatives.add(candidate);
                            if(alternatives.size()!=fetchList.size()) {
                                if(alternatives.isEmpty()) {
                                    if(delayedReveal!=null)reveal(delayedReveal);
                                    declinedRecoveries.add(sa);
                                    decision(p.getName(),profile,"zone-choice",fetchList.stream().map(Card::getName).toList(),"(leave in graveyard)",estimateOpponent(g,p),
                                        "Decline optional recovery to preserve the only Bringer for Spider-Man in hand.",
                                        map("priorityVariant",reanimatorScoreProfile(p,profile),"source",source,"abilityApi",str(sa.getApi()),"destination",str(destination),"optional",optional));
                                    return null;
                                }
                                decision(p.getName(),profile,"recovery-filter",fetchList.stream().map(Card::getName).toList(),"preserve graveyard payoff",estimateOpponent(g,p),
                                    "Keep the only graveyard payoff available for the copier in hand; choose an alternative.",
                                    map("priorityVariant",reanimatorScoreProfile(p,profile),"source",source,"abilityApi",str(sa.getApi()),"destination",str(destination),"optional",optional));
                                fetchList=alternatives;
                            }
                        }
                        Card selected=policyCard(fetchList,destination,sa,"zone-choice");
                        if(selected!=null){if(delayedReveal!=null)reveal(delayedReveal);return selected;}
                    }
                    return super.chooseSingleCardForZoneChange(destination, origin, sa, fetchList, delayedReveal, prompt, optional, decider);
                }
                @Override public <T extends GameEntity> T chooseSingleEntityForEffect(FCollectionView<T> options,DelayedReveal delayedReveal,
                        SpellAbility sa,String title,boolean optional,Player related,Map<String,Object> params) {
                    if(isReanimator(profile)&&sa!=null&&sa.getApi()==ApiType.Clone&&sa.getHostCard()!=null&&role(p,sa.getHostCard().getName(),"graveyard_copier")
                            &&!options.isEmpty()&&options.iterator().next() instanceof Card) {
                        CardCollection cards=new CardCollection();for(T option:options)if(option instanceof Card card)cards.add(card);
                        boolean redundant=cards.stream().allMatch(card->role(attributes(card),"graveyard_copier")&&number(attributes(card),"pilotCopyPriority",Integer.MAX_VALUE)<=2);
                        if(declineRedundantCopy(flag(object(attributes(sa.getHostCard()),"copy"),"optional"),!cards.isEmpty(),redundant)) {
                            if(delayedReveal!=null)reveal(delayedReveal);
                            decision(p.getName(),profile,"effect-choice",cards.stream().map(Card::getName).toList(),"(enter without copying)",estimateOpponent(g,p),
                                "An optional copy of another low-value copier adds no useful ability; retain the graveyard card.",
                                map("source",sa.getHostCard().getName(),"abilityApi",str(sa.getApi()),"destination","Battlefield","sourceWasCast",sa.getHostCard().wasCast()));
                            return null;
                        }
                        Card selected=policyCard(cards,ZoneType.Battlefield,sa,"effect-choice");
                        if(selected!=null){if(delayedReveal!=null)reveal(delayedReveal);return (T)selected;}
                    }
                    return super.chooseSingleEntityForEffect(options,delayedReveal,sa,title,optional,related,params);
                }
                private CardCollection discardChoices(Player discarder,SpellAbility sa,CardCollection offered,int min,int max,CardCollectionView visible) {
                    if(sa.getHostCard()!=null&&role(attributes(sa.getHostCard()),"creature_stun")&&role(attributes(sa.getHostCard()),"discard_outlet")
                            &&flag(object(attributes(sa.getHostCard()),"pilot"),"discardNonlandsForStun")&&offered.size()>=min&&g.getRegisteredPlayers().stream().filter(other->other!=p).anyMatch(other->other.getCardsIn(ZoneType.Battlefield).stream().anyMatch(Card::isCreature))) {
                        CardCollection nonlands=new CardCollection();for(Card card:offered)if(!card.isLand())nonlands.add(card);
                        int nonlandCount=Math.min(min,nonlands.size());
                        CardCollection chosen=new CardCollection();
                        if(nonlandCount>0)chosen.addAll(super.chooseCardsToDiscardFrom(discarder,sa,nonlands,nonlandCount,nonlandCount,visible));
                        CardCollection remaining=new CardCollection(offered);remaining.removeAll(chosen);
                        if(chosen.size()<min)chosen.addAll(super.chooseCardsToDiscardFrom(discarder,sa,remaining,min-chosen.size(),max-chosen.size(),visible));
                        if(chosen.size()==min&&chosen.stream().distinct().count()==min&&chosen.stream().allMatch(offered::contains)) {
                            decision(p.getName(),profile,"stun-discard",offered.stream().map(Card::getName).toList(),chosen.stream().map(Card::getName).toList().toString(),estimateOpponent(g,p),
                                "Prefer legal nonland discards for the annotated stun effect; retain Forge's valuation within those choices.",map("source",sa.getHostCard().getName(),"minimum",min,"maximum",max));
                            return chosen;
                        }
                    }
                    return super.chooseCardsToDiscardFrom(discarder,sa,offered,min,max,visible);
                }
                @Override public CardCollection chooseCardsToDiscardFrom(Player discarder,SpellAbility sa,CardCollection valid,int min,int max,CardCollectionView visible) {
                    if(isReanimator(profile)&&discarder==p&&sa!=null&&sa.getApi()==ApiType.Discard&&min==max&&min>0) {
                        boolean hasPayoffRoute=attributesByPlayer.getOrDefault(p.getName(),Map.of()).values().stream().anyMatch(attrs->role(attrs,"mass_reanimation"));
                        long copiers=p.getCardsIn(ZoneType.Hand).stream().filter(c->role(attributes(c),"graveyard_copier")).count();
                        long offeredCopiers=valid.stream().filter(card->role(attributes(card),"graveyard_copier")).count();
                        CardCollection alternatives=new CardCollection();boolean protectedCopier=false;
                        for(Card card:valid) {
                            boolean protect=role(attributes(card),"graveyard_copier")&&flag(object(attributes(card),"pilot"),"protectLastInHand");
                            if(protect&&!protectedCopier)protectedCopier=true;else alternatives.add(card);
                        }
                        boolean preserveCopier=protectLastCopierDiscard(hasPayoffRoute,copiers,protectedCopier&&offeredCopiers==copiers,alternatives.size(),min);
                        CardCollection offered=preserveCopier?alternatives:valid;
                        boolean payoffInGraveyard=p.getCardsIn(ZoneType.Graveyard).stream().anyMatch(c->role(attributes(c),"mass_reanimation"));
                        Card setup=offered.stream().filter(c->role(attributes(c),"discard_setup_target")&&role(attributes(c),"mass_reanimation")
                            &&enableLootDiscardSetup(flag(object(attributes(c),"pilot"),"discardForHeldCopier"),copiers>0,payoffInGraveyard,opponentHasGraveyardHate())).findFirst().orElse(null);
                        if(setup!=null) {
                            CardCollection remaining=new CardCollection(offered);remaining.remove(setup);
                            CardCollection chosen=new CardCollection();chosen.add(setup);
                            if(min>1)chosen.addAll(discardChoices(discarder,sa,remaining,min-1,max-1,visible));
                            if(chosen.size()==min&&chosen.stream().distinct().count()==min&&chosen.stream().allMatch(valid::contains)) {
                                decision(p.getName(),profile,"setup-discard",valid.stream().map(Card::getName).toList(),chosen.stream().map(Card::getName).toList().toString(),
                                    estimateOpponent(g,p),"Put an annotated payoff into the graveyard for a held copier; preserve the sole copier when alternatives allow it.",
                                    map("source",sa.getHostCard()==null?"":sa.getHostCard().getName(),"abilityApi",str(sa.getApi()),"minimum",min,"maximum",max));
                                return chosen;
                            }
                        }
                        if(preserveCopier) {
                            CardCollection chosen=discardChoices(discarder,sa,alternatives,min,max,visible);
                            if(chosen.size()>=min&&chosen.size()<=max&&chosen.stream().allMatch(valid::contains)) {
                                decision(p.getName(),profile,"protected-discard",valid.stream().map(Card::getName).toList(),chosen.stream().map(Card::getName).toList().toString(),
                                    estimateOpponent(g,p),"Keep the sole graveyard copier in hand for the deck's mass-reanimation route.",
                                    map("source",sa.getHostCard()==null?"":sa.getHostCard().getName(),"abilityApi",str(sa.getApi()),"minimum",min,"maximum",max));
                                return chosen;
                            }
                        }
                    }
                    if(isReanimator(profile)&&discarder==p&&sa!=null&&sa.getHostCard()!=null
                            &&role(p,sa.getHostCard().getName(),"creature_stun")&&role(p,sa.getHostCard().getName(),"discard_outlet")&&min==max&&min>0&&valid.size()>=min) {
                        CardCollection chosen=new CardCollection();
                        List<Card> ranked=new ArrayList<>(valid);
                        ranked.sort(Comparator.comparingInt((Card card)->{
                            if(role(attributes(card),"discard_setup_target"))return -100;
                            if(role(attributes(card),"graveyard_copier"))return -35;
                            if(card.isLand())return p.getCardsIn(ZoneType.Battlefield).stream().filter(Card::isLand).count()<3?100:10;
                            return card.getCMC();
                        }).thenComparing(card->card.getName()));
                        for(int i=0;i<min;i++)chosen.add(ranked.get(i));
                        decision(p.getName(),profile,"cryomancer-discard",valid.stream().map(Card::getName).toList(),
                            chosen.stream().map(Card::getName).toList().toString(),estimateOpponent(g,p),"Put a reanimation target into the graveyard while keeping early land drops.");
                        return chosen;
                    }
                    return super.chooseCardsToDiscardFrom(discarder,sa,valid,min,max,visible);
                }
                @Override public Player chooseStartingPlayer(boolean first){return g.getPlayers().get(starter);}
                @Override public boolean mulliganKeepHand(Player first,int tucked){
                    boolean keep=super.mulliganKeepHand(first,tucked);
                    acceptedByPolicy.put(p.getName(),keep);
                    emit(map("kind","mulligan_decision","player",p.getName(),"mulligans",p.getStats().getMulliganCount(),"policyAccepted",keep,
                        "detail",keep?"Pilot kept the hand.":"Pilot requested another mulligan."));return keep;
                }
            });return p;
        }
    }
    // Only one game executes at a time in each JVM. Loaded card/rules data is shared;
    // all match objects, bridge observations, RNG and mutable engine caches are fresh.
    static final Map<java.lang.reflect.Field,Integer> initialCounters = new LinkedHashMap<>();
    static boolean initialized;
    static void resetGameState() throws Exception {
        game=null;captureFailure=null;sequence=0;actualTurn=0;
        speakerDiscardHints.clear();publiclySeen.clear();personalTurns.clear();acceptedByPolicy.clear();
        attributesByPlayer.clear();optimizerPoliciesByPlayer.clear();optimizerFixturePolicy=null;
        AiCache.clear();
        for(String type:List.of("forge.game.Game","forge.game.spellability.SpellAbility",
                "forge.game.trigger.Trigger","forge.game.replacement.ReplacementEffect","forge.game.staticability.StaticAbility")) {
            java.lang.reflect.Field field=Class.forName(type).getDeclaredField("maxId");field.setAccessible(true);
            if(!initialCounters.containsKey(field))initialCounters.put(field,field.getInt(null));
            field.setInt(null,initialCounters.get(field));
        }
        java.lang.reflect.Field combat=Class.forName("forge.game.combat.CombatView").getDeclaredField("nextId");
        combat.setAccessible(true);combat.setInt(null,-2);
        java.lang.reflect.Field choices=Class.forName("forge.ai.ability.ChangeZoneAi").getDeclaredField("multipleCardsToChoose");
        choices.setAccessible(true);((CardCollection)choices.get(null)).clear();
    }
    static void runGame(JsonObject request) throws Exception {
            attemptId=request.get("attemptId").getAsString();turnLimit=request.get("turnLimit").getAsInt();
            System.setProperty("java.awt.headless","true");
            if(!initialized) {
                GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);initialized=true;
            }
            resetGameState();
            boolean performanceMode=request.has("performanceMode") && request.get("performanceMode").getAsBoolean();
            Spell.setPerformanceMode(performanceMode);
            StaticData.instance().setFilteredHandsEnabled(false);
            StaticData.instance().setMulliganRule(MulliganDefs.MulliganRule.London);
            MyRandom.setRandom(new Random(request.get("seed").getAsLong()));
            List<RegisteredPlayer> players=new ArrayList<>();
            Map<String,String> profiles=new LinkedHashMap<>();
            int starter=request.get("starter").getAsInt();
            for(int i=0;i<2;i++){
                Deck d=new Deck(i==0?"A":"B");
                Map<String,JsonObject> cardAttributes=new LinkedHashMap<>();
                for(JsonElement entry:request.getAsJsonArray(i==0?"deckA":"deckB")){
                    var o=entry.getAsJsonObject();String name=o.get("name").getAsString();int quantity=o.get("quantity").getAsInt();
                    var pc=StaticData.instance().getCommonCards().getCard(name);
                    if(pc==null)throw new IllegalArgumentException("Required card not loaded: "+name);
                    d.getMain().add(pc,quantity);
                    JsonObject attrs=o.has("attributes")&&o.get("attributes").isJsonObject()?o.getAsJsonObject("attributes").deepCopy():new JsonObject();
                    cardAttributes.put(name,attrs);
                }
                attributesByPlayer.put(i==0?"A":"B",cardAttributes);
                if(d.getMain().countAll()!=60)throw new IllegalArgumentException("Expected exactly 60 loaded main-deck cards.");
                String profile=request.has(i==0?"pilotA":"pilotB")?request.get(i==0?"pilotA":"pilotB").getAsString():"forge-default";
                if(!SUBTYPE_PARENTS.containsKey(profile)&&!Set.of(AGGRO,CONTROL,UNIFIED_REANIMATOR,"forge-default","reanimator-terror-v9","reanimator-sultai-v9","reanimator-cryomancer-v9","reanimator-shared-v9",
                    "reanimator-terror-v10","reanimator-sultai-v10","reanimator-cryomancer-v10","reanimator-shared-v10").contains(profile))
                    throw new IllegalArgumentException("Unknown pilot profile: "+profile);
                profiles.put(i==0?"A":"B",profile);
                String optimizerKey=i==0?"optimizerPolicyA":"optimizerPolicyB";
                if(request.has(optimizerKey)&&request.get(optimizerKey).isJsonObject()){
                    JsonObject optimizer=request.getAsJsonObject(optimizerKey);validateOptimizerPolicy(optimizer,profile);
                    optimizerPoliciesByPlayer.put(i==0?"A":"B",optimizer);
                }
                players.add(new RegisteredPlayer(d).setPlayer(new ForcedLobby(i==0?"A":"B",starter,profile)));
            }
            GameRules rules=new GameRules(GameType.Constructed);rules.setGamesPerMatch(1);rules.setPlayForAnte(false);
            rules.setAISideboardingEnabled(false);rules.setSideboardForAI(false);rules.setAllowCheatShuffle(false);
            Match match=new Match(rules,players,"Local Standard Bo1 diagnostic");
            game=match.createGame();game.setNoGUIUser();game.subscribeToEvents(new Listener());
            String policy=request.has("pilotVersion")?request.get("pilotVersion").getAsString():"forge-default-v1";
            emit(map("kind","ready","detail","Forge 2.0.15 / "+policy+"; Performance Mode "+(performanceMode?"on":"off")+"; diagnostic, pilot fidelity unvalidated."));
            for(var profile:profiles.entrySet())emit(map("kind","pilot_profile","player",profile.getKey(),"pilotProfile",profile.getValue(),"pilotVersion",policy));
            long started=System.nanoTime();match.startGame(game);
            if(captureFailure!=null)throw new IllegalStateException("Capture failed",captureFailure);
            var outcome=game.getOutcome();if(outcome==null)throw new IllegalStateException("Game returned without an engine outcome.");
            checkpoint("snapshot","terminal","Final engine state",null);
            String winner=outcome.isDraw()?null:outcome.getWinningLobbyPlayer().getName();
            String reason=outcome.getWinCondition().toString();
            List<String> lossReasons=new ArrayList<>();
            for(Player p:game.getRegisteredPlayers())if(p.getOutcome()!=null&&p.getOutcome().lossState!=null)lossReasons.add(p.getName()+": "+p.getOutcome().lossState);
            emit(map("kind","game_finished","winner",winner,"draw",outcome.isDraw(),"reason",reason+"; "+String.join(", ",lossReasons),
                "detail","DurationMs="+((System.nanoTime()-started)/1000000)));
    }
    public static void main(String[] args) {
        output=System.out;System.setOut(System.err);
        boolean worker=Arrays.asList(args).contains("--worker");
        try {
            if(worker) {
                BufferedReader requests=new BufferedReader(new InputStreamReader(System.in,StandardCharsets.UTF_8));
                String line;
                while((line=requests.readLine())!=null) {
                    runGame(JsonParser.parseString(line).getAsJsonObject());
                    // The boundary follows terminal capture and cleanup, and is transport
                    // metadata only: it is never included in a game's replay or checksum.
                    resetGameState();
                    output.println(json.toJson(map("kind","worker_complete","attemptId",attemptId,
                        "protocolVersion",1,"heapUsedBytes",Runtime.getRuntime().totalMemory()-Runtime.getRuntime().freeMemory())));
                    output.flush();
                }
            } else runGame(JsonParser.parseReader(new InputStreamReader(System.in,StandardCharsets.UTF_8)).getAsJsonObject());
            System.exit(0);
        }catch(Throwable failure){failure.printStackTrace(System.err);emit(map("kind","attempt_failed","reason",failure.toString()));System.exit(3);}
    }
}
