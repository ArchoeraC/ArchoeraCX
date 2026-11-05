import 'package:flutter/foundation.dart';

/// Tracks the coarse `launch.start` stages reported by the C# module.
///
/// The module reports one number per completed stage. A jump means the
/// optional repair stage was not needed, so the skipped step stays out of the
/// checklist instead of looking like a failure.
class LaunchProgressController extends ChangeNotifier {
  LaunchProgressController({this.total = 8});

  final int total;

  final Set<int> _reached = <int>{};
  final Set<int> _skipped = <int>{};
  int _furthest = 0;

  int get furthest => _furthest;
  Set<int> get reached => Set.unmodifiable(_reached);
  Set<int> get skipped => Set.unmodifiable(_skipped);
  bool get hasReports => _furthest > 0;

  void report(int done, int total) {
    if (done <= 0) return;
    final step = done > this.total ? this.total : done;
    if (step <= _furthest) return;

    if (_furthest > 0) {
      for (var skipped = _furthest + 1; skipped < step; skipped++) {
        _skipped.add(skipped);
      }
    }
    _reached.add(step);
    _furthest = step;
    notifyListeners();
  }
}
