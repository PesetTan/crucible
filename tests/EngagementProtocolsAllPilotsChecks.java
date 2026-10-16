import java.util.*;
import java.io.*;
import forge.*;
import forge.game.*;
import forge.game.player.*;
import forge.game.card.*;
import forge.game.zone.*;
import forge.game.phase.*;
import forge.game.spellability.*;
import forge.gui.GuiBase;
import forge.model.FModel;
import com.google.gson.*;

/** Real-engine adapters share a pure evaluator; no fixture reads concealed cards for decisions. */
public final class EngagementProtocolsAllPilotsChecks {
    static int checks;
    static void check(boolean ok,String why){checks++;if(!ok)throw new AssertionError(why);}
    static Card put(Player p,String name,ZoneType zone){return PilotAttributeChecks.put(p,name,zone);}
    static Game setup(String profile,String hidden,boolean enabled)throws Exception{
        ForgeBridge.resetGameState();
        Game g=PilotAttributeChecks.game(profile,PhaseType.MAIN1);
        if(enabled)ForgeBridge.optimizerPoliciesByPlayer.put("A",JsonParser.parseString("{\"version\":1,\"id\":\"fixture\",\"profile\":\""+profile+"\",\"tuning\":{}}").getAsJsonObject());
        Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);
        p.setLife(20,null);o.setLife(20,null);
        put(o,hidden,ZoneType.Hand);put(o,hidden,ZoneType.Library);
        ForgeBridge.attributesByPlayer.get("B").put(hidden,JsonParser.parseString("{\"roles\":[\"counterspell\"],\"pilot\":{\"priority\":999}}").getAsJsonObject());
        for(String land:List.of("Plains","Island","Swamp","Mountain","Forest"))for(int i=0;i<3;i++)put(p,land,ZoneType.Battlefield);
        String key=profile.equals(ForgeBridge.AGGRO)?"burn-v001":profile.equals(ForgeBridge.CONTROL)?"jace-control-v001":profile;
        ForgeBridge.attributesByPlayer.get("A").put("Grizzly Bears",JsonParser.parseString("{\"subtypePilot\":{\"developOnPass\":{\""+key+"\":20}}}").getAsJsonObject());
        ForgeBridge.attributesByPlayer.get("A").put("Glorious Anthem",JsonParser.parseString("{\"subtypePilot\":{\"developOnPass\":{\""+key+"\":100}}}").getAsJsonObject());
        put(p,"Glorious Anthem",ZoneType.Hand);put(p,"Grizzly Bears",ZoneType.Hand);
        return g;
    }
    static SpellAbility choose(Game g,String profile,List<SpellAbility> baseline){return ForgeBridge.subtypeDevelopmentOnPass(g,g.getPlayers().get(0),baseline,profile);}
    static String exercise(String profile,String hidden)throws Exception{
        Game g=setup(profile,hidden,true);Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);
        check(choose(g,profile,null).getHostCard().getName().equals("Glorious Anthem"),profile+" ordinary priority");
        put(o,"Cancel",ZoneType.Graveyard);for(String land:List.of("Island","Island","Plains"))put(o,land,ZoneType.Battlefield);
        check(choose(g,profile,null).getHostCard().getName().equals("Grizzly Bears"),profile+" observed counter route prefers lower commitment");
        p.setLife(8,null);put(o,"Grizzly Bears",ZoneType.Battlefield);
        var result=choose(g,profile,null);check(result.getHostCard().getName().equals("Grizzly Bears"),profile+" pressure prefers body");
        check(result.canPlay()&&result.canCastTiming(p)&&forge.ai.ComputerUtilCost.canPayCost(result,p,false),profile+" legal engine cast and payment");
        check(choose(g,profile,List.of(result))==null,profile+" retains selected Forge action");
        g.getPhaseHandler().devModeSet(PhaseType.COMBAT_BEGIN,p);check(choose(g,profile,null)==null,profile+" phase fallback");
        g.getPhaseHandler().devModeSet(PhaseType.MAIN1,o);check(choose(g,profile,null)==null,profile+" opponent turn fallback");
        g.getPhaseHandler().devModeSet(PhaseType.MAIN1,p);for(Card land:p.getCardsIn(ZoneType.Battlefield))land.setTapped(true);
        check(choose(g,profile,null)==null,profile+" no payment fallback");
        for(Card land:p.getCardsIn(ZoneType.Battlefield))land.setTapped(false);
        ForgeBridge.attributesByPlayer.get("A").clear();check(choose(g,profile,null)==null,profile+" absent metadata inert");
        return result.getHostCard().getName();
    }
    static void burn(String profile)throws Exception{
        Game g=setup(profile,"Plains",true);Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);o.setLife(3,null);
        put(p,"Tectonic Hazard",ZoneType.Hand);put(p,"Boltwave",ZoneType.Hand);
        for(String name:List.of("Tectonic Hazard","Boltwave"))ForgeBridge.attributesByPlayer.get("A").put(name,JsonParser.parseString("{\"aggro\":{\"castOpponentBurnOnPass\":true}}").getAsJsonObject());
        var result=ForgeBridge.aggroBurnOnPass(g,p,null,profile);
        check(result.getHostCard().getName().equals("Boltwave"),profile+" prefers predicted finishing burn over first offered damage");
        ForgeBridge.optimizerPoliciesByPlayer.clear();check(ForgeBridge.aggroBurnOnPass(g,p,null,profile).getHostCard().getName().equals("Tectonic Hazard"),profile+" preserves untuned burn order");
    }
    public static void main(String[] args)throws Exception{
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="all-protocol-fixture";
        Set<String> profiles=new TreeSet<>(ForgeBridge.SUBTYPE_PARENTS.keySet());profiles.add(ForgeBridge.AGGRO);profiles.add(ForgeBridge.CONTROL);
        check(ForgeBridge.EngagementProtocols.REGISTRY.size()==34,"32 current profiles, Reanimator preview and Forge baseline cataloged");
        check(ForgeBridge.EngagementProtocols.definitions("reanimator-v011").isEmpty(),"Reanimator legacy unchanged");
        for(String profile:profiles){
            check(!ForgeBridge.EngagementProtocols.definitions(profile).isEmpty(),profile+" declared set");
            if(!profile.equals(ForgeBridge.AGGRO))check(exercise(profile,"Plains").equals(exercise(profile,"Jeskai Revelation")),profile+" concealed hand/library/annotations invariant");
            Game g=setup(profile,"Plains",false);g.getPlayers().get(0).setLife(8,null);put(g.getPlayers().get(1),"Grizzly Bears",ZoneType.Battlefield);
            if(ForgeBridge.SUBTYPE_PARENTS.containsKey(profile))check(choose(g,profile,null).getHostCard().getName().equals("Glorious Anthem"),profile+" untuned choices unchanged");
            else check(choose(g,profile,null)==null,profile+" untuned broad fallback unchanged");
        }
        for(String profile:List.of("aggro-v001","burn-v001","creature-aggro-v001","allies-v001"))burn(profile);
        Game restricted=setup("artifacts-v001","Plains",true);Player opponent=restricted.getPlayers().get(1);
        put(opponent,"Negate",ZoneType.Graveyard);put(opponent,"Island",ZoneType.Battlefield);put(opponent,"Plains",ZoneType.Battlefield);
        check(!ForgeBridge.protocolObservation(restricted,restricted.getPlayers().get(0),false).counterRisk(),"Unrecognized printed counter shape preserves the existing observation boundary");
        // A typed public fact exercises the supported restricted-route contract without broadening v012's printed-shape parser.
        var observed=new ForgeBridge.PublicCard("Fixture restricted route","",false,2,false,false);
        observed.counters=List.of(new ForgeBridge.CounterRoute("Fixture restricted route",List.of(2),1,"Spell.Noncreature"));
        ForgeBridge.publiclySeen.put("B",new LinkedHashMap<>(Map.of(-1,observed)));
        check(choose(restricted,"artifacts-v001",null).getHostCard().getName().equals("Grizzly Bears"),"Typed restricted noncreature counter uses one shared rank vector in real mixed-permanent choices");
        Game g=setup("forge-default","Plains",true);check(choose(g,"forge-default",null)==null,"Forge baseline remains inherited");
        ForgeBridge.resetGameState();check(ForgeBridge.optimizerPoliciesByPlayer.isEmpty()&&ForgeBridge.activeProtocolsByPlayer.isEmpty()&&ForgeBridge.personalTurns.isEmpty(),"worker game reset clears policy/protocol/turn state");
        System.out.println("All-pilot Engagement Protocol checks passed: "+checks+" across 32 profiles.");
    }
}
