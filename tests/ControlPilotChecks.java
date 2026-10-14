import com.google.gson.JsonParser;
public final class ControlPilotChecks {
    static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    public static void main(String[] args) {
        check(ForgeBridge.controlSweepImproves(100,300,0,50),"Additional enemy removals justify a higher-value discard");
        check(!ForgeBridge.controlSweepImproves(100,100,20,0),"Do not change pairs without a better board trade");
        check(!ForgeBridge.controlSweepImproves(100,-100,0,0),"Friendly casualties can make a sweep harmful");
        check(!ForgeBridge.controlSweepImproves(100,150,0,250),"Preserve needed resources when gain is smaller");
        var attrs=JsonParser.parseString("{\"control\":{\"keepPriority\":40,\"graveyardExilePriority\":90},\"unknown\":true}").getAsJsonObject();
        check(ForgeBridge.controlKeepScore(attrs,false,2,1,5)==40,"Annotated win condition retention");
        check(ForgeBridge.controlKeepScore(attrs,true,2,1,5)==290,"Protect sole needed land");
        check(ForgeBridge.controlKeepScore(attrs,true,5,1,5)==40,"Land goal removes extra protection");
        check(ForgeBridge.controlKeepScore(attrs,true,2,2,5)==40,"Duplicate lands remain available");
        check(ForgeBridge.controlGraveyardPriority(attrs)==90,"Metadata drives public graveyard priority");
        check(ForgeBridge.controlGraveyardPriority(JsonParser.parseString("{}").getAsJsonObject())==0,"Unknown card falls back");
        System.out.println("Control policy guard checks passed; opposed replay tests cover integration.");
    }
}
