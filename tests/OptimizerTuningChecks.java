import com.google.gson.*;
public final class OptimizerTuningChecks {
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static JsonObject policy(String tuning){return JsonParser.parseString("{\"version\":1,\"id\":\""+"a".repeat(64)+"\",\"profile\":\"control-shared-v1\",\"tuning\":"+tuning+"}").getAsJsonObject();}
    static void rejects(JsonObject value,String profile){try{ForgeBridge.validateOptimizerPolicy(value,profile);}catch(IllegalArgumentException error){return;}throw new AssertionError("Invalid policy accepted");}
    public static void main(String[] args){
        var attrs=JsonParser.parseString("{\"control\":{\"keepPriority\":40},\"roles\":[\"win_condition\"]}").getAsJsonObject();
        var value=policy("{\"Example|control.keepPriority\":60}");ForgeBridge.validateOptimizerPolicy(value,"control-shared-v1");
        check(ForgeBridge.tunedAttributes(attrs,"Example",value).getAsJsonObject("control").get("keepPriority").getAsInt()==60,"Scoped numeric setting exercised");
        check(attrs.getAsJsonObject("control").get("keepPriority").getAsInt()==40,"Frozen base is unchanged");
        check(ForgeBridge.tunedAttributes(attrs,"Other",value)==attrs,"Other cards unchanged");
        check(ForgeBridge.tunedAttributes(new JsonObject(),"Example",value).size()==0,"Missing metadata inert");
        check(ForgeBridge.tunedAttributes(attrs,"Example",null)==attrs,"Absent policy preserves legacy behavior");
        rejects(value,"aggro-v001");rejects(policy("{\"Example|roles\":1}"),"control-shared-v1");
        rejects(policy("{\"Example|control.keepPriority\":1001}"),"control-shared-v1");
        rejects(policy("{\"Example|control.keepPriority\":1.5}"),"control-shared-v1");
        System.out.println("Optimizer tuning checks passed: isolation, bounds, fallback, frozen metadata.");
    }
}
