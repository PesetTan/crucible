// Minimal interop shim for the existing GPL-3.0 Forge engine; the application is C#.
// SPDX-License-Identifier: GPL-3.0-or-later
import com.google.common.eventbus.Subscribe;
import com.google.gson.*;
import forge.*;
import forge.ai.*;
import forge.deck.Deck;
import forge.game.*;
import forge.game.card.Card;
import forge.game.event.*;
import forge.game.player.*;
import forge.game.zone.ZoneType;
import forge.gui.GuiBase;
import forge.model.FModel;
import forge.util.MyRandom;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

public final class ForgeBridge {
    static final Gson json = new GsonBuilder().serializeNulls().disableHtmlEscaping().create();
    static PrintStream output;
    static String attemptId;
    static long sequence;
    static int actualTurn;
    static int turnLimit;
    static Game game;
    static Throwable captureFailure;
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
                if(e instanceof GameEventGameStarted started)emit(map("kind","game_started","starter",started.firstTurn().getName().equals("A")?0:1));
                if(e instanceof GameEventTurnBegan) {
                    actualTurn++;
                    if(actualTurn==1)for(Player opening:game.getRegisteredPlayers())emit(map("kind","opening_hand","player",opening.getName(),
                        "mulligans",opening.getStats().getMulliganCount(),"keptHandSize",opening.getStats().getOpeningHandSize(),
                        "policyAccepted",acceptedByPolicy.getOrDefault(opening.getName(),false),"detail","Final kept hand under London mulligans; Forge Default keep policy."));
                    Player p=game.getPhaseHandler().getPlayerTurn();
                    personalTurns.merge(p.getName(),1,Integer::sum);
                    if(actualTurn>turnLimit){emit(map("kind","attempt_failed","reason","Administrative turn limit reached."));System.exit(4);}
                }
                if(type.equals("GameEventPlayerPriority")||type.equals("GameEventCardStatsChanged")||type.equals("GameEventZone")||type.equals("GameEventCombatUpdate"))return;
                boolean capture=e instanceof GameEventTurnBegan || e instanceof GameEventTurnEnded || e instanceof GameEventTurnPhase ||
                    e instanceof GameEventGameStarted || e instanceof GameEventGameOutcome || e instanceof GameEventSpellAbilityCast ||
                    e instanceof GameEventSpellResolved || e instanceof GameEventPlayerLivesChanged || e instanceof GameEventCardChangeZone ||
                    e instanceof GameEventCardTapped || e instanceof GameEventCardCounters || e instanceof GameEventAttackersDeclared || e instanceof GameEventBlockersDeclared;
                if(capture)checkpoint("snapshot",type,e.toString(),e instanceof GameEventTurnBegan?game.getPhaseHandler().getPlayerTurn().isExtraTurn():null);
                else emit(map("kind","event","event",type,"detail",e.toString()));
            }catch(Throwable failure){captureFailure=failure;emit(map("kind","attempt_failed","reason","Replay capture failed: "+failure));System.exit(5);}
        }
    }
    public static final class ForcedLobby extends LobbyPlayerAi {
        private final int starter;
        public ForcedLobby(String name,int starter){super(name,null);this.starter=starter;setAiProfile("Default");}
        @Override public Player createIngamePlayer(Game g,int id){
            Player p=new Player(getName(),g,id);
            p.setFirstController(new PlayerControllerAi(g,p,this){
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
    public static void main(String[] args) {
        output=System.out;System.setOut(System.err);
        try {
            JsonObject request=JsonParser.parseReader(new InputStreamReader(System.in,StandardCharsets.UTF_8)).getAsJsonObject();
            attemptId=request.get("attemptId").getAsString();turnLimit=request.get("turnLimit").getAsInt();
            System.setProperty("java.awt.headless","true");
            GuiBase.setInterface(new GuiDesktop());
            FModel.initialize(null,null);
            StaticData.instance().setFilteredHandsEnabled(false);
            StaticData.instance().setMulliganRule(MulliganDefs.MulliganRule.London);
            MyRandom.setRandom(new Random(request.get("seed").getAsLong()));
            List<RegisteredPlayer> players=new ArrayList<>();
            int starter=request.get("starter").getAsInt();
            for(int i=0;i<2;i++){
                Deck d=new Deck(i==0?"A":"B");
                for(JsonElement entry:request.getAsJsonArray(i==0?"deckA":"deckB")){
                    var o=entry.getAsJsonObject();String name=o.get("name").getAsString();int quantity=o.get("quantity").getAsInt();
                    var pc=StaticData.instance().getCommonCards().getCard(name);
                    if(pc==null)throw new IllegalArgumentException("Required card not loaded: "+name);
                    d.getMain().add(pc,quantity);
                }
                if(d.getMain().countAll()!=60)throw new IllegalArgumentException("Expected exactly 60 loaded main-deck cards.");
                players.add(new RegisteredPlayer(d).setPlayer(new ForcedLobby(i==0?"A":"B",starter)));
            }
            GameRules rules=new GameRules(GameType.Constructed);rules.setGamesPerMatch(1);rules.setPlayForAnte(false);
            rules.setAISideboardingEnabled(false);rules.setSideboardForAI(false);rules.setAllowCheatShuffle(false);
            Match match=new Match(rules,players,"Local Standard Bo1 diagnostic");
            game=match.createGame();game.setNoGUIUser();game.subscribeToEvents(new Listener());
            emit(map("kind","ready","detail","Forge 2.0.15 / Default AI; diagnostic, pilot fidelity unvalidated."));
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
            System.exit(0);
        }catch(Throwable failure){failure.printStackTrace(System.err);emit(map("kind","attempt_failed","reason",failure.toString()));System.exit(3);}
    }
}
