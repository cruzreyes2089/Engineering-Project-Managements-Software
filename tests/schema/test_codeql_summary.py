import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("summarize_codeql", Path(__file__).resolve().parents[2] / "tools/security/summarize_codeql.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def fixture(results=None):
    return {"version": "2.1.0", "runs": [{"tool": {"driver": {"name": "CodeQL", "rules": [{"id": "cs/test"}]}},
            "invocations": [{"executionSuccessful": True}], "results": results or []}]}

class CodeQLSummaryTests(unittest.TestCase):
    def test_empty_results_not_clearance(self):
        report = m.summarize(fixture(), "csharp")
        self.assertEqual(report["resultCount"], 0)
        self.assertFalse(report["independentSecurityApproved"])
        self.assertFalse(report["releaseAuthorized"])

    def test_sparse_rule_metadata_is_not_query_coverage(self):
        doc = fixture()
        doc["runs"][0]["tool"]["driver"].pop("rules")
        report = m.summarize(doc, "python")
        self.assertEqual(report["ruleDescriptorCount"], 0)
        self.assertEqual(report["resultCount"], 0)
        self.assertFalse(report["queryCoverageVerified"])

    def test_rules_in_extensions(self):
        doc = fixture()
        doc["runs"][0]["tool"]["driver"].pop("rules")
        doc["runs"][0]["tool"]["extensions"] = [{"name": "query pack", "rules": [{"id": "cs/test"}]}]
        report = m.summarize(doc, "csharp")
        self.assertEqual(report["ruleDescriptorCount"], 1)
        self.assertFalse(report["queryCoverageVerified"])

    def test_location_and_no_message(self):
        report = m.summarize(fixture([{"ruleId": "cs/test", "level": "warning", "message": {"text": "DO NOT EMIT"},
                 "locations": [{"physicalLocation": {"artifactLocation": {"uri": "src/Test.cs"}, "region": {"startLine": 4}}}]}]), "csharp")
        self.assertEqual(report["findings"][0]["path"], "src/Test.cs")
        self.assertEqual(report["findings"][0]["line"], 4)
        self.assertNotIn("DO NOT EMIT", str(report))

    def test_invalid_paths_and_rule(self):
        report = m.summarize(fixture([{"ruleId": "::error::bad", "locations": [{"physicalLocation": {"artifactLocation": {"uri": "../private.txt"}}}]}]), "python")
        self.assertEqual(report["findings"][0]["path"], "[location unavailable]")
        self.assertEqual(report["findings"][0]["rule"], "[unrecognized rule]")

    def test_invalid_analysis(self):
        for doc in ({}, {"version": "2.1.0", "runs": []}):
            with self.assertRaises(ValueError):
                m.summarize(doc, "actions")
        doc = fixture()
        doc["runs"][0]["invocations"][0]["executionSuccessful"] = False
        with self.assertRaises(ValueError):
            m.summarize(doc, "actions")

    def test_diagnostics_counted_separately_from_findings(self):
        doc = fixture()
        invocation = doc["runs"][0]["invocations"][0]
        invocation["toolExecutionNotifications"] = [
            {"level": "note", "descriptor": {"id": "cs/diagnostics/extracted-files"}, "message": {"text": "PRIVATE"}},
            {"level": "note", "descriptor": {"id": "cs/diagnostics/extracted-files"}},
            {"level": "error", "descriptor": {"id": "cs/extraction-error"}}]
        invocation["toolConfigurationNotifications"] = [{"descriptor": {"id": "::invalid::"}}]
        report = m.summarize(doc, "csharp")
        self.assertEqual(report["resultCount"], 0)
        self.assertEqual(report["notificationCount"], 4)
        self.assertEqual(report["diagnosticLevels"], {"note": 2, "error": 1, "unspecified": 1})
        self.assertIn({"level": "note", "rule": "cs/diagnostics/extracted-files", "count": 2}, report["diagnosticGroups"])
        self.assertNotIn("PRIVATE", str(report))
        self.assertFalse(report["queryCoverageVerified"])

    def test_truncated_count(self):
        report = m.summarize(fixture([{"ruleId": "cs/test"}] * 251), "csharp")
        self.assertEqual(report["resultCount"], 251)
        self.assertTrue(report["truncated"])
        self.assertEqual(len(report["findings"]), 250)

if __name__ == "__main__":
    unittest.main()
