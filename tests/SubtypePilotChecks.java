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
import com.google.gson.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;

public final class SubtypePilotChecks {
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static Card put(Player p,String name,ZoneType zone) {
        var paper=StaticData.instance().getCommonCards().getCard(name);check(paper!=null,"Loaded card: "+name);
        Card c=Card.fromPaperCard(paper,p);p.getZone(zone).add(c);return c;
    }
    static String development(String profile,String source,String hidden,int lands,boolean metadata,PhaseType phase,boolean opponentTurn,boolean baseline,boolean duplicate) {
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int n=0;n<2;n++) {
            Deck d=new Deck("fixture");d.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(d).setPlayer(new ForgeBridge.ForcedLobby(n==0?"A":"B",0,n==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Subtype fixture").createGame();
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        p.setLife(20,null);opp.setLife(20,null);g.getPhaseHandler().devModeSet(phase,opponentTurn?opp:p);
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.publiclySeen.clear();
        ForgeBridge.attributesByPlayer.put("A",new LinkedHashMap<>());
        if(metadata)ForgeBridge.attributesByPlayer.get("A").put(source,JsonParser.parseString("{\"subtypePilot\":{\"developOnPass\":{\""+profile+"\":90}}}").getAsJsonObject());
        for(String land:List.of("Plains","Island","Swamp","Mountain","Forest"))for(int n=0;n<lands;n++)put(p,land,ZoneType.Battlefield);
        put(opp,hidden,ZoneType.Hand);put(opp,hidden,ZoneType.Library);
        Card card=put(p,source,ZoneType.Hand);
        if(duplicate)put(p,source,ZoneType.Battlefield);
        List<SpellAbility> prior=baseline?List.of(card.getSpellAbilities().get(0)):null;
        SpellAbility selected=ForgeBridge.subtypeDevelopmentOnPass(g,p,prior,profile);
        if(selected!=null) {
            check(selected.getHostCard()==card,"Choose from legally accessible own hand");
            check(ForgeBridge.subtypeDevelopmentShape(selected,profile)&&selected.canPlay()&&selected.canCastTiming(p)&&ComputerUtilCost.canPayCost(selected,p,false),"Engine legal shape/timing/payment");
        }
        return selected==null?"pass":selected.getHostCard().getName();
    }
    public static void main(String[] args)throws Exception {
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="subtype-fixture";
        if(args.length>2&&args[2].equals("--lute")) {
            check(development("spell-control-v001","Resonating Lute","Plains",10,true,PhaseType.MAIN1,false,false,false).equals("Resonating Lute"),"Five-color Control mana development");
            check(development("spell-control-v001","Resonating Lute","Plains",0,true,PhaseType.MAIN1,false,false,false).equals("pass"),"Five-color Control unpaid fallback");
            System.out.println("Supplemental Resonating Lute integration: 2 positions passed.");System.exit(0);
        }
        JsonArray registry=JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonArray();int cases=0;
        for(JsonElement row:registry) {
            JsonObject r=row.getAsJsonObject();String profile=r.get("profile").getAsString(),source=r.getAsJsonArray("cards").get(0).getAsString();
            String chosen=development(profile,source,"Plains",10,true,PhaseType.MAIN1,false,false,false);
            check(chosen.equals(source),"Exercise "+profile+" actual reviewed permanent "+source);cases++;
            check(chosen.equals(development(profile,source,"Jeskai Revelation",10,true,PhaseType.MAIN1,false,false,false)),"Concealed opponent hand/library invariance "+profile);cases++;
            check(development(profile,source,"Plains",0,true,PhaseType.MAIN1,false,false,false).equals("pass"),"No mana fallback "+profile);cases++;
            check(development(profile,source,"Plains",10,false,PhaseType.MAIN1,false,false,false).equals("pass"),"Missing metadata fallback "+profile);cases++;
            check(development(profile,source,"Plains",10,true,PhaseType.MAIN1,false,true,false).equals("pass"),"Preserve accepted Forge action "+profile);cases++;
            check(development(profile,source,"Plains",10,true,PhaseType.MAIN1,true,false,false).equals("pass"),"Opponent turn fallback "+profile);cases++;
            check(development(profile,source,"Plains",10,true,PhaseType.COMBAT_BEGIN,false,false,false).equals("pass"),"Wrong phase fallback "+profile);cases++;
            System.out.println(profile+": 7 real-engine fixtures passed");
        }
        check(development("angels-v001","Giada, Font of Hope","Plains",10,true,PhaseType.MAIN2,false,false,true).equals("pass"),"Avoid losing same-name legendary permanent");cases++;
        for(String source:List.of("Shock","Sheltered by Ghosts","Pyroclasm")) {
            check(development("auras-v001",source,"Plains",10,true,PhaseType.MAIN1,false,false,false).equals("pass"),"Unreviewed ability shape remains inert "+source);cases++;
        }
        check(development("forge-default","Hired Claw","Plains",10,true,PhaseType.MAIN1,false,false,false).equals("pass"),"Legacy profile remains unchanged");cases++;
        // Every immutable representative has 60 loaded cards, not just a supported fixture card.
        JsonArray inventory=JsonParser.parseString(Files.readString(Path.of(args[1]))).getAsJsonArray();
        for(JsonElement row:inventory) {
            JsonObject r=row.getAsJsonObject();int count=0;
            for(JsonElement entry:r.getAsJsonObject("manifest").getAsJsonArray("cards")) {
                JsonObject card=entry.getAsJsonObject();if(!card.get("zone").getAsString().equals("MainDeck"))continue;
                check(StaticData.instance().getCommonCards().getCard(card.get("name").getAsString())!=null,"Deck coverage "+card.get("name"));count+=card.get("quantity").getAsInt();
            }
            check(count==60,"60 cards in exact manifest "+r.get("id"));
        }
        System.out.println("Subtype integration checks passed: "+cases+" positions across "+registry.size()+" profiles; "+inventory.size()+" supplied manifests loaded.");System.exit(0);
    }
}
