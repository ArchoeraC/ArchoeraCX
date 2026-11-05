import 'package:archoera_cx/services/core/launch/launch_progress.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('LaunchProgressController', () {
    test('marks reported stages as reached', () {
      final progress = LaunchProgressController();

      progress.report(1, 8);
      progress.report(2, 8);

      expect(progress.furthest, 2);
      expect(progress.reached, <int>{1, 2});
      expect(progress.skipped, isEmpty);
    });

    test('marks an unreported stage as skipped when the next one arrives', () {
      final progress = LaunchProgressController();

      progress.report(5, 8);
      progress.report(7, 8);

      expect(progress.furthest, 7);
      expect(progress.reached, <int>{5, 7});
      expect(progress.skipped, <int>{6});
    });

    test('ignores duplicate and out-of-order reports', () {
      final progress = LaunchProgressController();
      var notifications = 0;
      progress.addListener(() => notifications++);

      progress.report(3, 8);
      progress.report(3, 8);
      progress.report(2, 8);

      expect(progress.furthest, 3);
      expect(progress.reached, <int>{3});
      expect(notifications, 1);
    });

    test('clamps reports above the expected total', () {
      final progress = LaunchProgressController();

      progress.report(12, 8);

      expect(progress.furthest, progress.total);
    });

    test('hasReports stays false until the first report', () {
      final progress = LaunchProgressController();

      expect(progress.hasReports, isFalse);
      progress.report(0, 8);
      expect(progress.hasReports, isFalse);
    });
  });
}
