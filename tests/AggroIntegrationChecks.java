import forge.*;
import forge.ai.*;
import forge.deck.Deck;
import forge.game.*;
import forge.game.card.*;
import forge.game.player.*;
import forge.game.zone.ZoneType;
import forge.game.phase.PhaseType;
import forge.gui.GuiBase;
import forge.model.FModel;
import forge.game.spellability.*;
import com.google.gson.JsonParser;
import java.io.*;
import java.util.*;

public final class AggroIntegrationChecks {
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static Card put(Player p,String name,ZoneType zone) {
        Card c=Card.fromPaperCard(StaticData.instance().getCommonCards().getCard(name),p);p.getZone(zone).add(c);return c;
    }
    static String burn(String source,String hidden,int lands,boolean enabled,PhaseType phase,boolean opponentTurn,boolean hasBaseline) {
        return burn(source,hidden,lands,enabled,phase,opponentTurn,hasBaseline,false);
    }
    static String burn(String source,String hidden,int lands,boolean enabled,PhaseType phase,boolean opponentTurn,boolean hasBaseline,boolean blocked) {
        return burn(ForgeBridge.AGGRO,source,hidden,lands,enabled,phase,opponentTurn,hasBaseline,blocked);
    }
    static String burn(String profile,String source,String hidden,int lands,boolean enabled,PhaseType phase,boolean opponentTurn,boolean hasBaseline,boolean blocked) {
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int n=0;n<2;n++) {
            Deck d=new Deck("fixture");d.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(d).setPlayer(new ForgeBridge.ForcedLobby(n==0?"A":"B",0,n==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Aggro fixture").createGame();
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        p.setLife(20,null);opp.setLife(20,null);g.getPhaseHandler().devModeSet(phase,opponentTurn?opp:p);
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.publiclySeen.clear();
        ForgeBridge.attributesByPlayer.put("A",new LinkedHashMap<>());
        ForgeBridge.attributesByPlayer.get("A").put(source,JsonParser.parseString("{\"aggro\":{\"castOpponentBurnOnPass\":"+enabled+"}}").getAsJsonObject());
        for(int n=0;n<lands;n++)put(p,"Mountain",ZoneType.Battlefield);
        put(opp,hidden,ZoneType.Hand);put(opp,hidden,ZoneType.Library);
        if(blocked)put(opp,"Platinum Emperion",ZoneType.Battlefield);
        Card c=put(p,source,ZoneType.Hand);
        SpellAbility baseline=c.getSpellAbilities().get(0);
        SpellAbility selected=ForgeBridge.aggroBurnOnPass(g,p,hasBaseline?List.of(baseline):null);
        boolean expected=!blocked&&enabled&&lands>0&&!opponentTurn&&!hasBaseline&&phase.isMain()&&(source.equals("Boltwave")||source.equals("Tectonic Hazard"));
        check((selected!=null)==expected,"Expected eligible burn selection/fallback: "+source+" lands="+lands+" phase="+phase);
        if(selected!=null)check(selected.canPlay()&&selected.canCastTiming(p)&&ComputerUtilCost.canPayCost(selected,p,false),"Actual engine timing and payment checks pass");
        return selected==null?"pass":selected.getHostCard().getName();
    }
    public static void main(String[] args) {
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="aggro-fixture";
        check(burn("Boltwave","Plains",1,true,PhaseType.MAIN1,false,false).equals(burn("Boltwave","Jeskai Revelation",1,true,PhaseType.MAIN1,false,false)),"Concealed opponent hand invariance");
        burn("Tectonic Hazard","Plains",1,true,PhaseType.MAIN2,false,false);
        burn("Boltwave","Plains",1,true,PhaseType.MAIN1,false,false,true);
        burn("Boltwave","Plains",0,true,PhaseType.MAIN1,false,false);
        burn("Boltwave","Plains",1,false,PhaseType.MAIN1,false,false);
        burn("Boltwave","Plains",1,true,PhaseType.MAIN1,false,true);
        burn("Boltwave","Plains",1,true,PhaseType.MAIN1,true,false);
        burn("Boltwave","Plains",1,true,PhaseType.COMBAT_BEGIN,false,false);
        burn("Shock","Plains",1,true,PhaseType.MAIN1,false,false);
        burn("Pyroclasm","Plains",2,true,PhaseType.MAIN1,false,false);
        System.out.println("Aggro integration checks passed: real ability shapes, engine timing/payment, metadata opt-in, baseline priority, concealed-hand invariance, wrong phase/player, targeted/self-damage fallback.");
        System.exit(0);
    }
}
