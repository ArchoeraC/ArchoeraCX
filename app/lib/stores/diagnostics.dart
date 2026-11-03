import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/diagnostics/crash_analysis.dart';

/// The last crash analysis that still needs a user-visible dialog.
///
/// The launch watcher writes it after the game exits; `AppShell` shows the
/// dialog and clears it. Keeping this outside the launch page means the report
/// is not lost when the user navigated away or the window was hidden.
final pendingCrashReportProvider =
    NotifierProvider<PendingCrashReportNotifier, CrashAnalysis?>(
      PendingCrashReportNotifier.new,
    );

class PendingCrashReportNotifier extends Notifier<CrashAnalysis?> {
  @override
  CrashAnalysis? build() => null;

  void show(CrashAnalysis analysis) {
    state = analysis;
  }

  void clear() {
    state = null;
  }
}
