import forge.game.zone.ZoneType;
import java.util.*;

public final class ReanimatorPilotChecks {
    static ForgeBridge.PublicCard card(String name,String text,boolean creature,int manaValue,boolean battlefield) {
        return new ForgeBridge.PublicCard(name,text,creature,manaValue,battlefield,false);
    }
    static void expect(String actual,String expected) {
        if(!actual.equals(expected))throw new AssertionError("Expected "+expected+", got "+actual);
    }
    static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    public static void main(String[] args) {
        expect(ForgeBridge.classifyPublicCards(List.of()).kind,"unknown");
        var early=card("Early Attacker","",true,1,true);
        var second=card("Second Attacker","",true,2,true);
        expect(ForgeBridge.classifyPublicCards(List.of(early)).kind,"unknown");
        expect(ForgeBridge.classifyPublicCards(List.of(early,second)).kind,"aggro");
        expect(ForgeBridge.classifyPublicCards(List.of(card("Counterspell","Counter target spell.",false,2,false))).kind,"control");
        expect(ForgeBridge.classifyPublicCards(List.of(card("Formidable Speaker","",true,3,true))).kind,"combo/reanimator");
        expect(ForgeBridge.classifyPublicCards(List.of(card("Kona, Rescue Beastie","",true,4,true),card("Kona, Rescue Beastie","",true,4,false))).kind,"combo");
        expect(ForgeBridge.classifyPublicCards(List.of(early,second,card("Formidable Speaker","",true,3,true))).kind,"unknown");
        var defense=com.google.gson.JsonParser.parseString("{\"roles\":[\"interaction\"],\"pilot\":{\"priority\":20,\"opponentBonuses\":{\"aggro\":65}}}").getAsJsonObject();
        var copier=com.google.gson.JsonParser.parseString("{\"roles\":[\"graveyard_copier\"],\"pilot\":{\"priority\":80},\"pilotDuplicateHandPriority\":2}").getAsJsonObject();
        var payoff=com.google.gson.JsonParser.parseString("{\"roles\":[\"mass_reanimation\"],\"requiresCastForEnterEffect\":true,\"pilotCastCopyPriority\":100}").getAsJsonObject();
        var aggro=ForgeBridge.classifyPublicCards(List.of(early,second));
        if(ForgeBridge.attributeScore(defense,"reanimator-shared-v9",aggro,ZoneType.Hand,false,false)<=ForgeBridge.attributeScore(copier,"reanimator-shared-v9",aggro,ZoneType.Hand,false,false))
            throw new AssertionError("Attribute priorities retain the configured defensive preference.");
        if(ForgeBridge.preparedComboScore(80,true,true,ZoneType.Hand,false,true)<=85)
            throw new AssertionError("A prepared payoff must favor its missing copier, independent of names.");
        if(ForgeBridge.preparedComboScore(2,true,true,ZoneType.Hand,true,true)!=2)
            throw new AssertionError("A held copier does not need another tutor.");
        if(ForgeBridge.preparedComboScore(80,false,true,ZoneType.Hand,false,true)!=80)
            throw new AssertionError("Prepared combo priority does not leak to unrelated effects.");
        if(ForgeBridge.copyEffectScore(5,payoff,forge.game.ability.ApiType.ChangeZone,ZoneType.Hand,true)!=5)
            throw new AssertionError("Recovery cannot use a copy-only priority.");
        if(ForgeBridge.copyEffectScore(5,payoff,forge.game.ability.ApiType.Clone,ZoneType.Battlefield,false)!=5)
            throw new AssertionError("An uncast copier does not get a cast-only payoff bonus.");
        if(ForgeBridge.copyEffectScore(5,payoff,forge.game.ability.ApiType.Clone,ZoneType.Battlefield,true)!=100)
            throw new AssertionError("Cast copies can use the annotated payoff.");

        com.google.gson.JsonObject copyOnly=com.google.gson.JsonParser.parseString("{\"pilotCopyPriority\":2}").getAsJsonObject();
        check(ForgeBridge.copyEffectScore(80,copyOnly,forge.game.ability.ApiType.Clone,ZoneType.Battlefield,true)==2,"Copying a copier has different value from fetching it");
        check(ForgeBridge.copyEffectScore(80,copyOnly,forge.game.ability.ApiType.ChangeZone,ZoneType.Hand,true)==80,"Hand recovery retains its caster value");
        com.google.gson.JsonObject castPayoff=com.google.gson.JsonParser.parseString("{\"roles\":[\"mass_reanimation\"],\"requiresCastForEnterEffect\":true,\"pilotCastCopyPriority\":100,\"pilotCopyPriority\":1}").getAsJsonObject();
        check(ForgeBridge.copyEffectScore(5,castPayoff,forge.game.ability.ApiType.Clone,ZoneType.Battlefield,true)==100,"Cast-only payoff priority still applies to a cast clone");
        check(ForgeBridge.protectLastCopierDiscard(true,1,true,3,2),"Keep one caster if the discard can be satisfied by alternatives");
        check(!ForgeBridge.protectLastCopierDiscard(true,1,true,1,2),"Mandatory discard counts take precedence");
        check(!ForgeBridge.protectLastCopierDiscard(true,2,true,3,1),"A duplicate caster can still be discarded");
        check(!ForgeBridge.protectLastCopierDiscard(false,1,true,3,2),"Only the annotated deck route enables protection");
        check(!ForgeBridge.protectLastCopierDiscard(true,1,false,3,2),"The protection attribute is opt-in");
        check(ForgeBridge.declineRedundantCopy(true,true,true),"Do not consume a graveyard caster for a redundant optional copy");
        check(!ForgeBridge.declineRedundantCopy(false,true,true),"Mandatory copies still require a selection");
        check(!ForgeBridge.declineRedundantCopy(true,true,false),"Useful options stay available");


  com.google.gson.JsonObject annotated=com.google.gson.JsonParser.parseString("{\"pilotDuplicateHandPriority\":2}").getAsJsonObject();
  check(ForgeBridge.duplicateHandScore(85,annotated,ZoneType.Hand,true)==2,"Opponent bonus must not erase duplicate penalty");
  check(ForgeBridge.duplicateHandScore(85,annotated,ZoneType.Hand,false)==85,"First copy keeps its value");
  check(ForgeBridge.duplicateHandScore(85,annotated,ZoneType.Battlefield,true)==85,"Copying value is independent of hand duplicates");
  check(ForgeBridge.duplicateHandScore(85,new com.google.gson.JsonObject(),ZoneType.Hand,true)==85,"Unannotated cards retain existing value");
  check(ForgeBridge.duplicateHandScore(1,annotated,ZoneType.Hand,true)==1,"Duplicate limit cannot raise a low score");

        check(ForgeBridge.enableTutorDiscardSetup(true,true,true,false,false),"A held copier and setup target enable the annotated tutor discard");
        check(!ForgeBridge.enableTutorDiscardSetup(false,true,true,false,false),"Setup is opt-in");
        check(!ForgeBridge.enableTutorDiscardSetup(true,false,true,false,false),"No held copier means no new setup override");
        check(!ForgeBridge.enableTutorDiscardSetup(true,true,false,false,false),"Need an actual setup card in own hand");
        check(!ForgeBridge.enableTutorDiscardSetup(true,true,true,true,false),"An existing graveyard payoff needs no setup override");
        check(!ForgeBridge.enableTutorDiscardSetup(true,true,true,false,true),"Public graveyard interference blocks the setup override");

  check(ForgeBridge.enableLootDiscardSetup(true,true,false,false),"Known payoff and held copier permit setup");
  check(!ForgeBridge.enableLootDiscardSetup(false,true,false,false),"Setup requires opt-in attribute");
  check(!ForgeBridge.enableLootDiscardSetup(true,false,false,false),"No copier means retain ordinary discard behavior");
  check(!ForgeBridge.enableLootDiscardSetup(true,true,true,false),"Existing payoff needs no additional setup");
  check(!ForgeBridge.enableLootDiscardSetup(true,true,false,true),"Public interference blocks setup");
  com.google.gson.JsonObject combatValue=com.google.gson.JsonParser.parseString("{\"pilotCopyPriority\":40}").getAsJsonObject();
  check(ForgeBridge.copyEffectScore(0,combatValue,forge.game.ability.ApiType.Clone,ZoneType.Battlefield,true)==40,"Annotated opponent creature has useful copy value");
  check(ForgeBridge.copyEffectScore(0,combatValue,forge.game.ability.ApiType.ChangeZone,ZoneType.Hand,true)==0,"Copy priority does not leak into hand recovery");


  check(ForgeBridge.protectLastCopierDiscard(true,2,true,4,2),"A double discard must not lose both copiers");
  check(!ForgeBridge.protectLastCopierDiscard(true,2,true,4,1),"Discarding one duplicate leaves one held");
  check(!ForgeBridge.protectLastCopierDiscard(true,1,true,1,2),"Forced count with no alternatives must keep legal fallback");
  com.google.gson.JsonObject selfExile=com.google.gson.JsonParser.parseString("{\"pilot\":{\"blocksGraveyardSetup\":false}}").getAsJsonObject();
  com.google.gson.JsonObject actualThreat=com.google.gson.JsonParser.parseString("{\"pilot\":{\"blocksGraveyardSetup\":true}}").getAsJsonObject();
  check(!ForgeBridge.blocksGraveyardSetup(selfExile,"Exile this card from your graveyard.",false),"Reviewed self-exile is not opposing graveyard interference");
  check(ForgeBridge.blocksGraveyardSetup(actualThreat,"Target card in a graveyard.",false),"Real annotated interference remains active");
  check(ForgeBridge.blocksGraveyardSetup(selfExile,"Exile target card from a graveyard.",true),"Cloned cards use current text rather than printed annotations");
  check(ForgeBridge.blocksGraveyardSetup(new com.google.gson.JsonObject(),"Exile target card from a graveyard.",false),"Unknown cards retain conservative fallback");
  check(ForgeBridge.preparedRecovery(true,true,true,4,false),"A ready recovery can find the missing copier");
  check(!ForgeBridge.preparedRecovery(true,true,true,3,false),"Early land recovery remains unchanged");
  check(!ForgeBridge.preparedRecovery(true,true,true,4,true),"Real visible graveyard interference blocks recovery priority");
  check(!ForgeBridge.preparedRecovery(false,true,true,4,false),"Prepared recovery is opt-in");

        // Hidden cards are intentionally absent from the classifier's input.
        var publicCards=List.of(early,second);
        expect(ForgeBridge.classifyPublicCards(publicCards).kind,ForgeBridge.classifyPublicCards(new ArrayList<>(publicCards)).kind);
        System.out.println("Reanimator policy checks passed.");
    }
}
