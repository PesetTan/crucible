import java.util.*;
import forge.*;
import java.io.*;
import forge.game.*;
import forge.game.player.*;
import forge.game.card.*;
import forge.game.zone.*;
import forge.game.phase.*;
import forge.game.spellability.*;
import forge.gui.GuiBase;
import forge.model.FModel;
import com.google.gson.*;

public final class EngagementProtocolChecks {
    static int checks;
    static void check(boolean ok,String message){checks++;if(!ok)throw new AssertionError(message);}
    static ForgeBridge.DecisionContext context(boolean counter,boolean hate,boolean urgent,boolean combo){return new ForgeBridge.DecisionContext(counter,hate,urgent,combo,3,List.of("public"));}
    static ForgeBridge.CandidateFeatures features(int base,ForgeBridge.CandidateFeature... flags){return new ForgeBridge.CandidateFeatures(1,base,Set.of(flags));}
    static ForgeBridge.ProtocolEvaluation rank(ForgeBridge.DecisionContext c,ForgeBridge.CandidateFeatures f){return ForgeBridge.EngagementProtocols.evaluate(ForgeBridge.EngagementProtocols.REANIMATOR,c,f);}
    static void core(){
        check(rank(context(false,false,false,false),features(0)).activeProtocols().equals(List.of("standard-execution")),"Unknown starts at Standard");
        check(ForgeBridge.EngagementProtocols.definitions("reanimator-v011").isEmpty(),"Legacy has no protocols");
        check(ForgeBridge.EngagementProtocols.compare(rank(context(true,false,false,false),features(2,ForgeBridge.CandidateFeature.DEVELOPMENT)),rank(context(true,false,false,false),features(100,ForgeBridge.CandidateFeature.COUNTER_EXPOSED)))>0,"Counterplay prefers development");
        check(!rank(context(true,false,true,false),features(0)).activeProtocols().contains("counterplay"),"Urgency disables delay");
        check(!rank(context(true,false,false,true),features(0)).activeProtocols().contains("counterplay"),"Race disables delay");
        var all=context(true,true,true,true);
        check(rank(all,features(0)).activeProtocols().equals(List.of("graveyard-security","stabilize","race","standard-execution")),"Composition order");
        check(ForgeBridge.EngagementProtocols.compare(rank(all,features(1,ForgeBridge.CandidateFeature.DEFENSIVE)),rank(all,features(100,ForgeBridge.CandidateFeature.COMPLETE_COMBO)))>0,"Survival precedes race");
        check(ForgeBridge.EngagementProtocols.compare(rank(all,features(1)),rank(all,features(100,ForgeBridge.CandidateFeature.GRAVEYARD_EXPOSED,ForgeBridge.CandidateFeature.DEFENSIVE)))>0,"Interference precedes optional preferences");
        check(ForgeBridge.EngagementProtocols.compare(rank(all,features(10)),rank(all,features(10)))==0,"Stable ties");
        var extended=new ArrayList<>(ForgeBridge.EngagementProtocols.REANIMATOR);
        extended.add(new ForgeBridge.ProtocolDefinition("fixture","Fixture","Always","Prefer development",50,c->true,Map.of(ForgeBridge.CandidateFeature.DEVELOPMENT,1),2));
        check(ForgeBridge.EngagementProtocols.evaluate(extended,all,features(0,ForgeBridge.CandidateFeature.DEVELOPMENT)).rank().get(0)==1,"A new table entry needs no evaluator changes");
        check(ForgeBridge.EngagementProtocols.delayLimit(extended,all)==2,"Delay limits come from declarations, not protocol-specific branches");
        var memory=new ForgeBridge.EngagementProtocols.Memory();check(memory.mayDefer(4,3,1),"Initial allowance");memory.deferred(4,3);memory.deferred(4,4);
        check(memory.mayDefer(4,3,1),"Repeated priority same turn");check(!memory.mayDefer(4,4,1),"Cannot renew allowance");check(memory.mayDefer(5,4,1),"Per-card memory");
        check(ForgeBridge.publicManaMatches(List.of(2,2,1),List.of(2,2),1),"UU plus generic");
        check(!ForgeBridge.publicManaMatches(List.of(2,1,1),List.of(2,2),1),"Wrong colors");
        check(!ForgeBridge.publicManaMatches(List.of(2,2),List.of(2,2),1),"Insufficient generic");
        check(!ForgeBridge.publicManaMatches(List.of(3),List.of(2,1),0),"One source cannot pay twice");
    }
    static Card put(Player p,String n,ZoneType z){return PilotAttributeChecks.put(p,n,z);}
    @SuppressWarnings("unchecked")
    static List<SpellAbility> choose(Player p,List<SpellAbility> original)throws Exception {
        var method=p.getController().getClass().getDeclaredMethod("protocolSpells",List.class);method.setAccessible(true);return (List<SpellAbility>)method.invoke(p.getController(),original);
    }
    static Game setup(String hidden,boolean alternate){
        ForgeBridge.activeProtocolsByPlayer.clear();ForgeBridge.personalTurns.clear();
        Game g=PilotAttributeChecks.game("reanimator-v012",PhaseType.MAIN1);Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);
        var attrs=ForgeBridge.attributesByPlayer.get("A");
        attrs.put("Superior Spider-Man",JsonParser.parseString("{\"roles\":[\"graveyard_copier\"],\"pilot\":{\"priority\":80}}").getAsJsonObject());
        attrs.put("Bringer of the Last Gift",JsonParser.parseString("{\"roles\":[\"mass_reanimation\"]}").getAsJsonObject());
        attrs.put("Formidable Speaker",JsonParser.parseString("{\"roles\":[\"creature_tutor\"],\"pilot\":{\"priority\":45}}").getAsJsonObject());
        for(String land:List.of("Island","Island","Swamp","Forest","Forest"))put(p,land,ZoneType.Battlefield);
        for(String land:List.of("Island","Island","Plains"))put(o,land,ZoneType.Battlefield);
        put(o,"Cancel",ZoneType.Graveyard);put(o,hidden,ZoneType.Hand);put(o,hidden,ZoneType.Library);
        ForgeBridge.attributesByPlayer.get("B").put(hidden,JsonParser.parseString("{\"roles\":[\"counterspell\"],\"pilot\":{\"variant\":\"terror\"}}").getAsJsonObject());
        put(p,"Bringer of the Last Gift",ZoneType.Graveyard);put(p,"Superior Spider-Man",ZoneType.Hand);
        if(alternate)put(p,"Formidable Speaker",ZoneType.Hand);
        ForgeBridge.personalTurns.put("A",3);return g;
    }
    static SpellAbility copier(Player p){var c=p.getCardsIn(ZoneType.Hand).stream().filter(x->x.getName().equals("Superior Spider-Man")).findFirst().orElseThrow();var sa=c.getSpellPermanent();sa.setActivatingPlayer(p);return sa;}
    static String integration(String hidden,boolean alternate)throws Exception {
        Game g=setup(hidden,alternate);Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);var original=copier(p);
        check(ForgeBridge.protocolObservation(g,p,true).counterRisk(),"Actual Cancel route recognized");
        var first=choose(p,List.of(original));String selected=first==null?"wait":first.get(0).getHostCard().getName();
        check(selected.equals(alternate?"Formidable Speaker":"wait"),"Safer development or bounded wait");
        var repeated=choose(p,List.of(original));check((repeated==null?"wait":repeated.get(0).getHostCard().getName()).equals(selected),"Repeated priority is stable");
        ForgeBridge.personalTurns.put("A",4);check(choose(p,List.of(original)).get(0)==original,"Next own turn commits");
        p.setLife(8,null);put(o,"Grizzly Bears",ZoneType.Battlefield);
        check(ForgeBridge.protocolObservation(g,p,true).urgentPressure(),"Real public pressure activates Stabilize");
        check(choose(p,List.of(original)).get(0)==original,"Pressure removes discretionary delay");
        put(o,"Soul-Guide Lantern",ZoneType.Battlefield);check(ForgeBridge.protocolObservation(g,p,true).graveyardInterference(),"Visible graveyard interference");
        put(o,"Formidable Speaker",ZoneType.Graveyard);put(o,"Bringer of the Last Gift",ZoneType.Graveyard);check(ForgeBridge.protocolObservation(g,p,true).comboOpponent(),"Public combo evidence");
        return selected;
    }
    static void choices() throws Exception {
        Game g=setup("Plains",false);Player p=g.getPlayers().get(0),o=g.getPlayers().get(1);
        var attrs=ForgeBridge.attributesByPlayer.get("A");
        attrs.put("Grizzly Bears",JsonParser.parseString("{\"pilot\":{\"priority\":100}}").getAsJsonObject());
        attrs.put("Wistfulness",JsonParser.parseString("{\"pilot\":{\"priority\":20,\"opponentBonuses\":{\"aggro\":10}}}").getAsJsonObject());
        Card source=put(p,"Formidable Speaker",ZoneType.Battlefield);
        SpellAbility tutor=forge.game.ability.AbilityFactory.getAbility(source.getSVar("TrigSearch"),source);tutor.setActivatingPlayer(p);
        Card defense=put(p,"Wistfulness",ZoneType.Library),value=put(p,"Grizzly Bears",ZoneType.Library);
        CardCollection offered=new CardCollection();offered.add(value);offered.add(defense);
        check(p.getController().chooseSingleCardForZoneChange(ZoneType.Hand,List.of(ZoneType.Library),tutor,offered,null,"Tutor",false,p)==value,"Standard tutor preserves priority");
        p.setLife(8,null);put(o,"Grizzly Bears",ZoneType.Battlefield);
        check(p.getController().chooseSingleCardForZoneChange(ZoneType.Hand,List.of(ZoneType.Library),tutor,offered,null,"Tutor",false,p)==defense,"Stabilize changes real tutor choice");
        Card copier=p.getCardsIn(ZoneType.Hand).getFirst();
        copier=p.getCardsIn(ZoneType.Hand).stream().filter(c->c.getName().equals("Superior Spider-Man")).findFirst().orElseThrow();
        var copy=copier.getReplacementEffects().get(0).ensureAbility();copy.setActivatingPlayer(p);
        CardCollection grave=new CardCollection();grave.add(put(p,"Grizzly Bears",ZoneType.Graveyard));Card graveDefense=put(p,"Wistfulness",ZoneType.Graveyard);grave.add(graveDefense);
        check(p.getController().chooseSingleEntityForEffect(grave,null,copy,"Copy",false,p,Map.of())==graveDefense,"Stabilize changes real copy choice");
        Card defenseHand=put(p,"Wistfulness",ZoneType.Hand),valueHand=put(p,"Grizzly Bears",ZoneType.Hand);
        CardCollection discards=new CardCollection();discards.add(defenseHand);discards.add(valueHand);
        var selectedDiscard=p.getController().chooseCardsToDiscardFrom(p,tutor,discards,1,1,discards);
        check(selectedDiscard.getFirst()==valueHand,"Stabilize retains defense during a real discard choice: "+selectedDiscard+"; context "+ForgeBridge.protocolObservation(g,p,true)+"; attrs "+attrs.get("Wistfulness"));
        CardCollection forced=new CardCollection();forced.add(copier);
        check(p.getController().chooseCardsToDiscardFrom(p,tutor,forced,1,1,forced).size()==1,"Mandatory discard count wins over preferences");
    }
    public static void main(String[] args)throws Exception {
        core();System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="protocol-fixture";
        for(boolean alternative:List.of(true,false))check(integration("Plains",alternative).equals(integration("Negate",alternative)),"Concealed hand/library/metadata invariance");
        var g=setup("Plains",false);var p=g.getPlayers().get(0);var o=g.getPlayers().get(1);
        check(choose(p,null)==null,"Forge-pass combo respects Counterplay");
        ForgeBridge.personalTurns.put("A",4);check(choose(p,null).get(0).getHostCard().getName().equals("Superior Spider-Man"),"Forge-pass delay also expires");
        g.getPhaseHandler().devModeSet(PhaseType.UPKEEP,p);check(choose(p,null)==null,"Wrong phase retains fallback");
        g.getPhaseHandler().devModeSet(PhaseType.MAIN1,p);
        for(Card land:p.getCardsIn(ZoneType.Battlefield))land.setTapped(true);
        check(choose(p,null)==null,"Cannot force a spell without payment");
        for(Card c:o.getCardsIn(ZoneType.Graveyard))c.setFaceDown(true);
        check(!ForgeBridge.protocolObservation(g,p,true).counterRisk(),"Face-down cards are unavailable");
        choices();
        ForgeBridge.resetGameState();check(ForgeBridge.activeProtocolsByPlayer.isEmpty()&&ForgeBridge.publiclySeen.isEmpty()&&ForgeBridge.personalTurns.isEmpty(),"Worker reset clears protocol state");
        System.out.println("Engagement Protocol checks passed: "+checks);
    }
}
