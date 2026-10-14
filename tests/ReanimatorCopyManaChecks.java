import forge.*;
import forge.card.MagicColor;
import forge.deck.Deck;
import forge.game.*;
import forge.game.card.*;
import forge.game.mana.Mana;
import forge.game.player.*;
import forge.game.zone.ZoneType;
import forge.game.spellability.*;
import forge.gui.GuiBase;
import forge.model.FModel;
import com.google.gson.*;
import java.io.*;
import java.util.*;

public final class ReanimatorCopyManaChecks {
    static int positions;
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static Card put(Player p,String name,ZoneType zone) {
        Card c=Card.fromPaperCard(StaticData.instance().getCommonCards().getCard(name),p);p.getZone(zone).add(c);return c;
    }
    static String choose(String profile,String target,String colors,boolean cast,boolean metadata,String hidden,boolean mismatched) {
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int i=0;i<2;i++) {
            Deck d=new Deck("fixture");d.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(d).setPlayer(new ForgeBridge.ForcedLobby(i==0?"A":"B",0,i==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Copy mana fixture").createGame();
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.publiclySeen.clear();
        Map<String,JsonObject> attrs=new LinkedHashMap<>();ForgeBridge.attributesByPlayer.put("A",attrs);
        attrs.put("Superior Spider-Man",JsonParser.parseString("{\"roles\":[\"graveyard_copier\"]}").getAsJsonObject());
        attrs.put("Formidable Speaker",JsonParser.parseString("{\"roles\":[\"creature_tutor\"],\"pilot\":{\"priority\":45}}").getAsJsonObject());
        JsonObject targetAttrs=JsonParser.parseString("{\"pilot\":{\"priority\":85}}").getAsJsonObject();
        if(metadata) {
            JsonArray gates=new JsonArray();
            for(String color:target.equals("Wistfulness")?List.of("Green","Blue"):List.of("Blue","Black"))
                gates.add("CastSA>Count$Adamant_2."+(mismatched?"Red":color)+".2.0");
            JsonObject gate=new JsonObject();gate.add("conditions",gates);gate.addProperty("inactivePriority",0);targetAttrs.add("pilotCopyManaGate",gate);
        }
        attrs.put(target,targetAttrs);
        put(opp,hidden,ZoneType.Hand);put(opp,hidden,ZoneType.Library);
        Card copier=put(p,"Superior Spider-Man",ZoneType.Hand);SpellAbility spell=copier.getSpellPermanent();spell.setActivatingPlayer(p);
        if(cast) {
            copier.setCastSA(spell);copier.setCastFrom(p.getZone(ZoneType.Hand));
            for(char color:colors.toCharArray())spell.getPayingMana().add(new Mana(MagicColor.fromName(color),copier,null,p));
            check(copier.wasCast(),"Actual engine cast identity");
        }
        SpellAbility choice=copier.getReplacementEffects().get(0).ensureAbility();choice.setActivatingPlayer(p);
        check(choice.getApi()==forge.game.ability.ApiType.Clone,"Actual printed replacement ability");
        Card offered=put(p,target,ZoneType.Graveyard),speaker=put(p,"Formidable Speaker",ZoneType.Graveyard);
        CardCollection options=new CardCollection();options.add(offered);options.add(speaker);
        Card selected=p.getController().chooseSingleEntityForEffect(options,null,choice,"Copy",true,p,Map.of());
        check(selected!=null&&options.contains(selected),"Select a legally offered card");positions++;
        return selected.getName();
    }
    static String variantChoice(String profile,String variant,String hidden,boolean annotated) {
        Game g=PilotAttributeChecks.game(profile,forge.game.phase.PhaseType.MAIN1);
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        Map<String,JsonObject> own=ForgeBridge.attributesByPlayer.get("A");
        own.put("Superior Spider-Man",JsonParser.parseString("{\"roles\":[\"graveyard_copier\"]}").getAsJsonObject());
        own.put("Formidable Speaker",JsonParser.parseString("{\"pilot\":{\"priority\":20}}").getAsJsonObject());
        String target=variant.equals("cryomancer")?"Seasoned Cryomancer":variant.equals("sultai")?"Jadzi, Steward of Fate":"Terror of the Peaks";
        own.put(target,JsonParser.parseString("{\"pilot\":{\"priority\":0,"+(annotated?"\"variant\":\""+variant+"\",":"")+"\"variantPriorities\":{\""+variant+"\":38}}}").getAsJsonObject());
        // Different opposing configured metadata and concealed contents cannot select our priority table.
        ForgeBridge.attributesByPlayer.get("B").put("concealed",JsonParser.parseString("{\"pilot\":{\"variant\":\"cryomancer\"}}").getAsJsonObject());
        put(opp,hidden,ZoneType.Hand);put(opp,hidden,ZoneType.Library);
        Card copier=put(p,"Superior Spider-Man",ZoneType.Hand);
        SpellAbility ability=copier.getReplacementEffects().get(0).ensureAbility();ability.setActivatingPlayer(p);
        CardCollection offered=new CardCollection();offered.add(put(p,target,ZoneType.Graveyard));offered.add(put(p,"Formidable Speaker",ZoneType.Graveyard));
        Card selected=p.getController().chooseSingleEntityForEffect(offered,null,ability,"Copy",true,p,Map.of());
        check(selected!=null&&offered.contains(selected),"Priority choice remains engine-offered");positions++;
        return selected.getName();
    }
    static boolean supportsUnified() {
        try{ForgeBridge.class.getDeclaredField("UNIFIED_REANIMATOR");return true;}catch(NoSuchFieldException missing){return false;}
    }
    public static void main(String[] args) {
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="copy-mana-fixture";
        for(String profile:supportsUnified()?List.of("reanimator-terror-v10","reanimator-v011"):List.of("reanimator-terror-v10"))
        for(String target:List.of("Wistfulness","Deceit")) {
            check(choose(profile,target,"UBCG",true,true,"Plains",false).equals("Formidable Speaker"),"Inactive copied entry effect loses to actual value");
            check(choose(profile,target,"UUBG",true,true,"Plains",false).equals(target),"Two actual blue mana activates reviewed entry effect");
            check(choose(profile,target,target.equals("Wistfulness")?"UGGB":"UBBC",true,true,"Plains",false).equals(target),"Second actual mana gate activates");
            check(choose(profile,target,"UBCG",false,true,"Plains",false).equals("Formidable Speaker"),"Reanimated copier has no cast payment");
            check(choose(profile,target,"UBCG",true,false,"Plains",false).equals(target),"Absent metadata fallback");
            check(choose(profile,target,"UBCG",true,true,"Plains",true).equals(target),"Mismatched printed trigger fallback");
            check(choose("reanimator-terror-v9",target,"UBCG",true,true,"Plains",false).equals(target),"Legacy profile retains behavior with identical metadata");
            check(choose(profile,target,"UBCG",true,true,"Jeskai Revelation",false).equals("Formidable Speaker"),"Changed concealed opponent hand/library does not affect choice");
        }
        if(supportsUnified())for(String variant:List.of("cryomancer","sultai","terror")) {
            String legacy=variantChoice("reanimator-"+variant+"-v10",variant,"Plains",true);
            check(variantChoice("reanimator-v011",variant,"Plains",true).equals(legacy),"Unified identity retains annotated variant choice");
            check(variantChoice("reanimator-v011",variant,"Jeskai Revelation",true).equals(legacy),"Opponent concealed cards do not change priority choice");
            check(variantChoice("reanimator-v011",variant,"Plains",false).equals(legacy),"Historical own-card metadata fallback retains choice");
        }
        System.out.println("Reanimator copy mana checks passed: "+positions+" actual engine choice positions.");System.exit(0);
    }
}
