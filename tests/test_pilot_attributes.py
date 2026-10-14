import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("evaluate_pilots", Path(__file__).resolve().parents[1] / "scripts/evaluate-pilots.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class AttributeScoringChecks(unittest.TestCase):
    def case(self, dimension, status, actual="hold"):
        return dict(profile="fixture", dimension=dimension, status=status, actual=actual, origin="Forge Default")

    def score(self, cases, dimension):
        return next(d for d in module.summarize(cases, ["fixture"])[0]["dimensions"] if d["key"] == dimension)

    def test_errors_are_not_losses(self):
        value = self.score([self.case("combat", "passed"), self.case("combat", "failed"), self.case("combat", "error")], "combat")
        self.assertEqual((value["passed"], value["total"], value["errors"], value["percent"]), (1, 2, 1, 50))

    def test_missing_checks_are_unknown(self):
        value = self.score([], "combo")
        self.assertFalse(value["measured"])
        self.assertIsNone(value["percent"])
        value = self.score([self.case("combo", "error")], "combo")
        self.assertFalse(value["measured"])
        self.assertEqual(value["errors"], 1)

    def test_aggression_is_an_observation(self):
        value = self.score([self.case("aggression", "observed", "attack"), self.case("aggression", "observed", "hold"), self.case("aggression", "error")], "aggression")
        self.assertEqual((value["passed"], value["total"], value["percent"]), (1, 2, 50))
        self.assertEqual(value["kind"], "Observed tendency")

    def test_exact_profiles_stay_separate(self):
        rows = [self.case("resources", "passed"), dict(self.case("resources", "failed"), profile="other-v2")]
        reports = module.summarize(rows, ["fixture", "other-v2"])
        self.assertEqual([next(d for d in r["dimensions"] if d["key"] == "resources")["percent"] for r in reports], [100, 0])

if __name__ == "__main__":
    unittest.main()
