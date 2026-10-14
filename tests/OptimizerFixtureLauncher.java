import com.google.gson.*;
import java.nio.file.*;
import java.util.Arrays;

/** Runs existing real engine fixtures with the candidate's scoped policy. */
public final class OptimizerFixtureLauncher {
    public static void main(String[] args) throws Exception {
        ForgeBridge.optimizerFixturePolicy=JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        ForgeBridge.validateOptimizerPolicy(ForgeBridge.optimizerFixturePolicy,ForgeBridge.optimizerFixturePolicy.get("profile").getAsString());
        Class.forName(args[1]).getMethod("main",String[].class).invoke(null,(Object)Arrays.copyOfRange(args,2,args.length));
    }
}
