import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('analysis', Path(__file__).resolve().parents[1]/'tools/analyze_teleop_latency.py')
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


class TimingTests(unittest.TestCase):
    def samples(self, valid=1, move=True):
        rows = []
        for i in range(150):
            t = i*.02
            target = .05 if move and t>=.6 else 0
            actual = .05*max(0,min(1,(t-.8)/.2)) if move else 0
            row = dict(unity_time_s=t, feedback_age_s=.01, command_valid=valid)
            for axis in 'xyz':
                row['target_'+axis] = target if axis=='x' else 0
                row['feedback_'+axis] = actual if axis=='x' else 0
            rows.append(row)
        return rows
    def test_known_delay(self):
        result = analysis.analyze(self.samples())
        self.assertEqual(len(result),1)
        self.assertAlmostEqual(result[0]['response_s'],.22,places=5)
        self.assertTrue(.34<=result[0]['arrival_after_target_stop_s']<=.4)
        self.assertTrue(result[0]['under_1s'])
    def test_no_motion_not_a_pass(self):
        self.assertEqual(analysis.analyze(self.samples(move=False)),[])
    def test_invalid_tracking_not_a_pass(self):
        self.assertEqual(analysis.analyze(self.samples(valid=0)),[])

if __name__=='__main__':unittest.main()
