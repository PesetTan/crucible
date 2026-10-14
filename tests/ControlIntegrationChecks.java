import forge.*;
import forge.ai.*;
import forge.deck.Deck;
import forge.game.*;
import forge.game.card.*;
import forge.game.cost.*;
import forge.game.player.*;
import forge.game.zone.ZoneType;
import forge.gui.GuiBase;
import forge.model.FModel;
import forge.game.spellability.*;
import com.google.gson.JsonParser;
import java.io.*;
import java.util.*;

public final class ControlIntegrationChecks {
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static Card put(Player p,String name,ZoneType zone) {
        Card c=Card.fromPaperCard(StaticData.instance().getCommonCards().getCard(name),p);p.getZone(zone).add(c);return c;
    }
    static Set<String> sweep(String concealed,boolean friendly) { return sweep(ForgeBridge.CONTROL,concealed,friendly); }
    static Set<String> sweep(String profile,String concealed,boolean friendly) {
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int n=0;n<2;n++){
            Deck deck=new Deck("fixture");deck.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(deck).setPlayer(new ForgeBridge.ForcedLobby(n==0?"A":"B",0,n==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Control fixture").createGame();
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);
        g.getPhaseHandler().devModeSet(forge.game.phase.PhaseType.MAIN1,p);
        p.setLife(20,null);opp.setLife(20,null);
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.attributesByPlayer.put("A",new LinkedHashMap<>());
        ForgeBridge.attributesByPlayer.get("A").put("Ill-Timed Explosion",JsonParser.parseString("{\"control\":{\"discardMaxManaSweeper\":true,\"landGoal\":5}}").getAsJsonObject());
        put(p,"Mountain",ZoneType.Hand);put(p,"Plains",ZoneType.Hand);put(p,"Jeskai Revelation",ZoneType.Hand);put(p,"Inevitable Defeat",ZoneType.Hand);
        for(int n=0;n<5;n++)put(p,"Mountain",ZoneType.Battlefield);
        put(opp,"Superior Spider-Man",ZoneType.Battlefield);put(opp,concealed,ZoneType.Hand);
        if(friendly){put(p,"Superior Spider-Man",ZoneType.Battlefield);put(p,"Superior Spider-Man",ZoneType.Battlefield);}
        Card host=put(p,"Ill-Timed Explosion",ZoneType.Graveyard);
        SpellAbility sa=host.getSpellAbilities().get(0).getSubAbility();sa.setActivatingPlayer(p);
        check(sa.getApi()==forge.game.ability.ApiType.ImmediateTrigger,"Fixture must use actual Forge sweeper ability");
        CostDiscard cost=new CostDiscard("2","Card",null);
        PaymentDecision base=new AiCostDecision(p,sa,true).visit(cost);
        PaymentDecision selected=p.getController().getCostDecisionMaker(p,sa,true,"").visit(cost);
        check(selected!=null&&selected.cards.size()==2&&p.getCardsIn(ZoneType.Hand).containsAll(selected.cards),"Select two legal held cards");
        if(friendly)check(new HashSet<>(selected.cards).equals(new HashSet<>(base.cards)),"Friendly casualties retain baseline discards");
        else check(selected.cards.stream().mapToInt(Card::getCMC).max().orElse(0)>=4,"Discard enough mana value for a visible 4/4");
        return new HashSet<>(selected.cards.stream().map(Card::getName).toList());
    }
    static void exile() { exile(ForgeBridge.CONTROL); }
    static void exile(String profile) {
        List<RegisteredPlayer> players=new ArrayList<>();
        for(int n=0;n<2;n++) {
            Deck deck=new Deck("exile fixture");deck.getMain().add(StaticData.instance().getCommonCards().getCard("Plains"),60);
            players.add(new RegisteredPlayer(deck).setPlayer(new ForgeBridge.ForcedLobby(n==0?"A":"B",0,n==0?profile:"forge-default")));
        }
        Game g=new Match(new GameRules(GameType.Constructed),players,"Exile fixture").createGame();
        Player p=g.getPlayers().get(0),opp=g.getPlayers().get(1);g.getPhaseHandler().devModeSet(forge.game.phase.PhaseType.MAIN1,p);
        p.setLife(20,null);opp.setLife(20,null);
        ForgeBridge.attributesByPlayer.clear();ForgeBridge.attributesByPlayer.put("A",new LinkedHashMap<>());ForgeBridge.attributesByPlayer.put("B",new LinkedHashMap<>());
        ForgeBridge.attributesByPlayer.get("A").put("Soul-Guide Lantern",JsonParser.parseString("{\"control\":{\"prioritizeGraveyardExile\":true}}").getAsJsonObject());
        // An unfamiliar metadata priority proves the selection is not hardcoded to a payoff name.
        ForgeBridge.attributesByPlayer.get("B").put("Plains",JsonParser.parseString("{\"control\":{\"graveyardExilePriority\":1000}}").getAsJsonObject());
        Card desired=put(opp,"Plains",ZoneType.Graveyard);put(opp,"Bringer of the Last Gift",ZoneType.Graveyard);put(p,"Jeskai Revelation",ZoneType.Graveyard);
        Card host=put(p,"Soul-Guide Lantern",ZoneType.Battlefield);
        SpellAbility sa=forge.game.ability.AbilityFactory.getAbility(host.getSVar("TrigChange"),host);sa.setActivatingPlayer(p);
        check(p.getController().chooseTargetsFor(sa),"Forge must prepare legal graveyard targets");
        check(sa.getTargets().getFirstTargetedCard()==desired&&sa.isTargetNumberValid(),"Public metadata priority chooses an engine-legal enemy card");
    }
    public static void main(String[] args) {
        System.setProperty("java.awt.headless","true");GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="control-fixture";
        check(sweep("Plains",false).equals(sweep("Jeskai Revelation",false)),"Changing concealed opponent hand must not change discard selection");
        sweep("Plains",true);
        exile();
        System.out.println("Control integration fixtures passed: real sweeper ability, legal payment, friendly casualties, concealed-hand invariance, legal public exile target.");
        System.exit(0);
    }
}
