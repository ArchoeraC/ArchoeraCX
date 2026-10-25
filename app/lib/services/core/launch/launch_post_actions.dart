import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../../app/desktop_integration.dart';
import '../core_client.dart';
import 'launch_log.dart';

/// Desktop window actions taken after `launch.start` returns.
@immutable
class PostLaunchPlan {
  const PostLaunchPlan({
    required this.hideWindow,
    required this.monitorGameExit,
  });

  const PostLaunchPlan.none() : hideWindow = false, monitorGameExit = false;

  /// Hide the launcher window once the game is running.
  final bool hideWindow;

  /// Watch the launch log for the game process to exit, then quit.
  final bool monitorGameExit;
}

/// Maps the desktop launch toggles to window actions.
///
/// Returns [PostLaunchPlan.none] when the start failed: a failed launch must
/// never hide the launcher or quit it for a process that never existed.
PostLaunchPlan postLaunchPlan({
  required bool succeeded,
  required bool hideAfterLaunch,
  required bool quitAfterGameExit,
}) {
  if (!succeeded) return const PostLaunchPlan.none();
  return PostLaunchPlan(
    hideWindow: hideAfterLaunch,
    monitorGameExit: quitAfterGameExit,
  );
}

/// Quits the launcher once the started game process exits.
///
/// The C# module pushes appended log lines through the ABI progress callback,
/// so [LaunchLogTail.done] completes when the process ends without any Dart
/// timer or polling loop. Log chunks are intentionally ignored; only the end
/// of the stream matters here.
///
/// A follow also ends when the module reaches its maximum follow duration
/// (six hours), not only when the process exits. After every completed follow
/// this asks the module whether the instance is still running and follows
/// again when it is, so a long session is never mistaken for an exit. A failed
/// follow is checked once against the same running state; if that also fails,
/// the launcher stays running instead of guessing that the game is gone.
Future<void> quitWhenGameExits(CoreClient client, String instanceId) {
  Future<bool> stillRunning() async {
    final logs = await client.listLaunchLogs(instanceId);
    return logs.running;
  }

  return runExitMonitor(
    () => client.tailLaunchLog(instanceId: instanceId),
    quit: quitApplication,
    stillRunning: stillRunning,
  );
}

/// Follows a launch log to its end, then calls [quit] when the game is gone.
///
/// Extracted from [quitWhenGameExits] so the wake-driven completion, the
/// follow-limit restart and the failure paths can be tested without a native
/// core or a real game process. When [stillRunning] is null the completed
/// follow is treated as an exit.
@visibleForTesting
Future<void> runExitMonitor(
  Future<LaunchLogTail> Function() startTail, {
  required Future<void> Function() quit,
  Future<bool> Function()? stillRunning,
}) async {
  while (true) {
    StreamSubscription<String>? subscription;
    try {
      final LaunchLogTail tail = await startTail();
      subscription = tail.chunks.listen((_) {}, onError: (_, _) {});
      await tail.done;
    } catch (error, stackTrace) {
      _logFailure(
        'cannot follow the game log for exit detection',
        error,
        stackTrace,
      );
      // A very short session can end before the follow starts. Ask once so
      // that a crash on startup still honours the quit preference; if the
      // module cannot tell us either, leave the launcher running.
      if (stillRunning != null) {
        try {
          if (!await stillRunning()) await quit();
        } catch (statusError, statusStack) {
          _logFailure(
            'cannot verify whether the game is still running',
            statusError,
            statusStack,
          );
        }
      }
      return;
    } finally {
      await subscription?.cancel();
    }

    if (stillRunning != null) {
      try {
        if (await stillRunning()) continue;
      } catch (error, stackTrace) {
        _logFailure(
          'cannot verify whether the game is still running',
          error,
          stackTrace,
        );
        return;
      }
    }

    await quit();
    return;
  }
}

void _logFailure(String message, Object error, StackTrace stackTrace) {
  if (!kDebugMode) return;
  debugPrint('launch post-actions: $message: $error\n$stackTrace');
}
