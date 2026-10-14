import forge.*;
import forge.ai.*;
import forge.deck.Deck;
import forge.game.*;
import forge.game.card.*;
import forge.game.combat.Combat;
import forge.game.player.*;
import forge.game.phase.PhaseType;
import forge.game.zone.ZoneType;
import forge.game.spellability.*;
import forge.gui.GuiBase;
import forge.model.FModel;
import forge.util.MyRandom;
import com.google.gson.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;

/** Diagnostic positions, never a simulated-game outcome or an overall skill rating. */
public final class PilotAttributeChecks {
    static final List<Map<String,Object>> results=new ArrayList<>();
    interface Probe { String run() throws Exception; }
    static void test(String profile,String dimension,String id,String origin,String kind,String expected,Probe probe) {
        Map<String,Object> row=new LinkedHashMap<>();
        row.put("profile",profile);row.put("dimension",dimension);row.put("id",id);
        row.put("origin",origin);row.put("kind",kind);row.put("expected",expected);
        try { MyRandom.setRandom(new Random(20261013));String actual=probe.run();row.put("actual",actual);row.put("status",kind.equals("observation")?"observed":expected.equals(actual)?"passed":"failed"); }
        catch(AssertionError failure){row.put("status",kind.equals("observation")?"error":"failed");row.put("actual",failure.toString());}
        catch(Exception error){row.put("status","error");row.put("actual",error.toString());}
        results.add(row);
    }
    static Game game(String profile,PhaseType phase) {
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.publiclySeen.clear();
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int i=0;i<2;i++) {
            Deck d=new Deck("scorecard fixture");d.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(d).setPlayer(new ForgeBridge.ForcedLobby(i==0?"A":"B",0,i==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Pilot attribute fixture").createGame();
        for(Player p:g.getPlayers()){p.setLife(20,null);ForgeBridge.attributesByPlayer.put(p.getName(),new LinkedHashMap<>());}
        g.getPhaseHandler().devModeSet(phase,g.getPlayers().get(0));return g;
    }
    static Card put(Player p,String name,ZoneType zone) {
        var paper=StaticData.instance().getCommonCards().getCard(name);
        if(paper==null)throw new IllegalArgumentException("Fixture card missing: "+name);
        Card c=Card.fromPaperCard(paper,p);p.getZone(zone).add(c);c.setSickness(false);return c;
    }
    static String attack(String profile,String blocker,int opponentLife) {
        Game g=game(profile,PhaseType.COMBAT_DECLARE_ATTACKERS);Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        opp.setLife(opponentLife,null);Card bear=put(p,"Grizzly Bears",ZoneType.Battlefield);
        if(blocker!=null)put(opp,blocker,ZoneType.Battlefield);
        Combat combat=new Combat(p);g.getPhaseHandler().setCombat(combat);p.getController().declareAttackers(p,combat);
        return combat.isAttacking(bear)?"attack":"hold";
    }
    static String combo(String profile,boolean cast) {
        Game g=game(profile,PhaseType.MAIN1);Player p=g.getPlayers().get(0);
        var attrs=ForgeBridge.attributesByPlayer.get("A");
        attrs.put("Superior Spider-Man",JsonParser.parseString("{\"roles\":[\"graveyard_copier\"]}").getAsJsonObject());
        attrs.put("Bringer of the Last Gift",JsonParser.parseString("{\"roles\":[\"mass_reanimation\"],\"pilot\":{\"priority\":10},\"requiresCastForEnterEffect\":true,\"pilotCastCopyPriority\":100}").getAsJsonObject());
        attrs.put("Formidable Speaker",JsonParser.parseString("{\"pilot\":{\"priority\":45}}").getAsJsonObject());
        Card copier=put(p,"Superior Spider-Man",ZoneType.Hand);SpellAbility spell=copier.getSpellPermanent();spell.setActivatingPlayer(p);
        if(cast){copier.setCastSA(spell);copier.setCastFrom(p.getZone(ZoneType.Hand));}
        SpellAbility ability=copier.getReplacementEffects().get(0).ensureAbility();ability.setActivatingPlayer(p);
        CardCollection offered=new CardCollection();offered.add(put(p,"Bringer of the Last Gift",ZoneType.Graveyard));offered.add(put(p,"Formidable Speaker",ZoneType.Graveyard));
        Card selected=p.getController().chooseSingleEntityForEffect(offered,null,ability,"Copy",true,p,Map.of());
        return selected==null?"none":selected.getName();
    }
    static String adapt(String profile,String kind) {
        var cards=new ArrayList<ForgeBridge.PublicCard>();
        if(kind.equals("aggro")){cards.add(new ForgeBridge.PublicCard("Public creature","",true,1,true,false));cards.add(new ForgeBridge.PublicCard("Other public creature","",true,2,true,false));}
        if(kind.equals("control"))cards.add(new ForgeBridge.PublicCard("Public spell","Counter target spell.",false,2,false,false));
        return ForgeBridge.classifyPublicCards(cards).kind;
    }
    public static void main(String[] args)throws Exception {
        if(args.length==1&&args[0].equals("--classification-checks")){
            test("fixture","combat","accepted","Forge Default","engine position","attack",()->"attack");
            test("fixture","combat","asserted-failure","Forge Default","engine position","attack",()->{throw new AssertionError("unacceptable decision");});
            test("fixture","combat","execution-error","Forge Default","engine position","attack",()->{throw new IOException("engine failed");});
            test("fixture","aggression","tendency","Forge Default","observation","attack or hold",()->"hold");
            test("fixture","aggression","invalid-observation","Forge Default","observation","attack or hold",()->{throw new AssertionError("invalid position");});
            var expected=List.of("passed","failed","error","observed","error");
            for(int i=0;i<expected.size();i++)if(!results.get(i).get("status").equals(expected.get(i)))throw new AssertionError("Incorrect result classification "+i);
            System.out.println("Result classification checks passed.");System.exit(0);
        }
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="attribute-fixture";
        JsonArray registry=JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonArray();
        List<String> profiles=new ArrayList<>(List.of("forge-default",ForgeBridge.AGGRO,ForgeBridge.CONTROL));
        for(String revision:args[2].split(",")) {
            if(revision.equals("11"))profiles.add("reanimator-v011");
            else for(String variant:List.of("terror","sultai","cryomancer","shared"))profiles.add("reanimator-"+variant+"-v"+revision);
        }
        for(JsonElement item:registry)profiles.add(item.getAsJsonObject().get("profile").getAsString());
        Map<String,String> settings=new LinkedHashMap<>();Player baseline=game("forge-default",PhaseType.MAIN1).getPlayers().get(0);
        for(AiProps key:AiProps.values())settings.put(key.name(),AiProfileUtil.getProperty(baseline,key));
        for(String profile:profiles) {
            for(String[] fixture:new String[][]{{"unblocked",null,"20","attack"},{"lethal",null,"2","attack"},{"certain-loss","Colossal Dreadmaw","20","hold"}}) {
                String id=fixture[0];String expected=fixture[3];
                test(profile,"combat",id,"Forge Default","engine position",expected,()->attack(profile,fixture[1],Integer.parseInt(fixture[2])));
            }
            // A trade is a tendency observation, not a universally correct decision.
            test(profile,"aggression","equal-trade","Forge Default","observation","attack or hold",()->attack(profile,"Grizzly Bears",20));
            if(ForgeBridge.isReanimator(profile)) {
                for(boolean cast:List.of(true,false))test(profile,"combo",cast?"cast-copy-payoff":"uncast-copy-value","Crucible override","engine position",cast?"Bringer of the Last Gift":"Formidable Speaker",()->combo(profile,cast));
                for(String kind:List.of("unknown","aggro","control"))test(profile,"adaptation","public-classifier-"+kind,"Crucible override","policy guard",kind,()->adapt(profile,kind));
                for(String target:List.of("Wistfulness","Deceit")) {
                    // Use the same desired value choice across revisions; preserving a legacy weakness is not a pass.
                    test(profile,"resources","copy-payment-inactive-"+target,"Crucible override","engine position","Formidable Speaker",()->ReanimatorCopyManaChecks.choose(profile,target,"UBCG",true,true,"Plains",false));
                    test(profile,"resources","copy-payment-active-"+target,"Crucible override","engine position",target,()->ReanimatorCopyManaChecks.choose(profile,target,"UUBG",true,true,"Plains",false));
                }
            }
            if(ForgeBridge.usesAggro(profile)) {
                test(profile,"interaction","reviewed-burn","Crucible override","engine hook","Boltwave",()->AggroIntegrationChecks.burn(profile,"Boltwave","Plains",1,true,PhaseType.MAIN1,false,false,false));
                test(profile,"resources","unpaid-burn","Crucible override","engine hook","pass",()->AggroIntegrationChecks.burn(profile,"Boltwave","Plains",0,true,PhaseType.MAIN1,false,false,false));
            }
            if(ForgeBridge.usesControl(profile)) {
                test(profile,"interaction","public-sweeper","Crucible shared Control hook","engine position","checked",()->{ControlIntegrationChecks.sweep(profile,"Plains",false);return "checked";});
                test(profile,"resources","friendly-sweeper","Crucible shared Control hook","engine position","checked",()->{ControlIntegrationChecks.sweep(profile,"Plains",true);return "checked";});
                test(profile,"interaction","public-exile","Crucible shared Control hook","engine position","checked",()->{ControlIntegrationChecks.exile(profile);return "checked";});
            }
            if(ForgeBridge.SUBTYPE_PARENTS.containsKey(profile)) {
                String source="";for(JsonElement item:registry)if(item.getAsJsonObject().get("profile").getAsString().equals(profile))source=item.getAsJsonObject().getAsJsonArray("cards").get(0).getAsString();
                final String permanent=source;
                test(profile,"resources","permanent-unpaid","Crucible override","engine hook","pass",()->SubtypePilotChecks.development(profile,permanent,"Plains",0,true,PhaseType.MAIN1,false,false,false));
                test(profile,"interaction","permanent-main-phase","Crucible override","engine hook",source,()->SubtypePilotChecks.development(profile,permanent,"Plains",10,true,PhaseType.MAIN1,false,false,false));
            }
            System.err.println("Evaluated "+profile);
        }
        Files.writeString(Path.of(args[1]),new GsonBuilder().setPrettyPrinting().create().toJson(Map.of("settings",settings,"cases",results)));
        System.exit(0);
    }
}
