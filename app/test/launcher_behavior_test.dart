import 'dart:async';

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/router.dart';
import 'package:archoera_cx/services/core/launch/launch_log.dart';
import 'package:archoera_cx/services/core/launch/launch_post_actions.dart';
import 'package:archoera_cx/stores/platform.dart';
import 'package:archoera_cx/stores/preferences.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<ProviderContainer> _pumpSettings(
  WidgetTester tester, {
  Size size = const Size(1380, 900),
  bool desktop = true,
}) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        desktopPlatformProvider.overrideWithValue(desktop),
        routerProvider.overrideWithValue(
          buildAppRouter(initialLocation: '/settings'),
        ),
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  await tester.tap(find.text('Launch').first);
  await tester.pumpAndSettle();
  return ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)));
}

void main() {
  group('launch behavior preferences', () {
    test('both switches default to off', () {
      const prefs = UiPreferences();
      expect(prefs.hideAfterLaunch, isFalse);
      expect(prefs.quitAfterGameExit, isFalse);
    });

    test('copyWith overrides only the requested flag', () {
      const both = UiPreferences(
        hideAfterLaunch: true,
        quitAfterGameExit: true,
      );
      final hiddenOnly = both.copyWith(quitAfterGameExit: false);
      expect(hiddenOnly.hideAfterLaunch, isTrue);
      expect(hiddenOnly.quitAfterGameExit, isFalse);

      final neither = both.copyWith(
        hideAfterLaunch: false,
        quitAfterGameExit: false,
      );
      expect(neither.hideAfterLaunch, isFalse);
      expect(neither.quitAfterGameExit, isFalse);
      // Unrelated values survive copyWith.
      expect(neither.startPage, both.startPage);
    });

    test('controller saves both flags', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{});
      final container = ProviderContainer();
      addTearDown(container.dispose);
      container.read(preferencesProvider);
      await Future<void>.delayed(Duration.zero);

      final notifier = container.read(preferencesProvider.notifier);
      await notifier.setHideAfterLaunch(true);
      await notifier.setQuitAfterGameExit(true);

      final stored = await SharedPreferences.getInstance();
      expect(stored.getBool('ui.hideAfterLaunch'), isTrue);
      expect(stored.getBool('ui.quitAfterGameExit'), isTrue);
    });

    test('controller restores persisted flags on build', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{
        'ui.hideAfterLaunch': true,
        'ui.quitAfterGameExit': true,
      });
      final container = ProviderContainer();
      addTearDown(container.dispose);

      container.read(preferencesProvider);
      await Future<void>.delayed(Duration.zero);

      final prefs = container.read(preferencesProvider);
      expect(prefs.hideAfterLaunch, isTrue);
      expect(prefs.quitAfterGameExit, isTrue);
    });
  });

  group('post-launch plan', () {
    test('a failed start never hides or quits', () {
      final plan = postLaunchPlan(
        succeeded: false,
        hideAfterLaunch: true,
        quitAfterGameExit: true,
      );
      expect(plan.hideWindow, isFalse);
      expect(plan.monitorGameExit, isFalse);
    });

    test('each switch maps independently after a successful start', () {
      final hideOnly = postLaunchPlan(
        succeeded: true,
        hideAfterLaunch: true,
        quitAfterGameExit: false,
      );
      expect(hideOnly.hideWindow, isTrue);
      expect(hideOnly.monitorGameExit, isFalse);

      final quitOnly = postLaunchPlan(
        succeeded: true,
        hideAfterLaunch: false,
        quitAfterGameExit: true,
      );
      expect(quitOnly.hideWindow, isFalse);
      expect(quitOnly.monitorGameExit, isTrue);

      final both = postLaunchPlan(
        succeeded: true,
        hideAfterLaunch: true,
        quitAfterGameExit: true,
      );
      expect(both.hideWindow, isTrue);
      expect(both.monitorGameExit, isTrue);

      final neither = postLaunchPlan(
        succeeded: true,
        hideAfterLaunch: false,
        quitAfterGameExit: false,
      );
      expect(neither.hideWindow, isFalse);
      expect(neither.monitorGameExit, isFalse);
    });
  });

  group('exit monitor', () {
    LaunchLogTail finishedTail() {
      final chunks = StreamController<String>();
      final done = Completer<void>();
      unawaited(chunks.close());
      done.complete();
      return LaunchLogTail(
        chunks: chunks.stream,
        done: done.future,
        stop: () {},
      );
    }

    test('quits only after the launch log tail completes', () async {
      final chunks = StreamController<String>();
      final done = Completer<void>();
      addTearDown(chunks.close);
      final tail = LaunchLogTail(
        chunks: chunks.stream,
        done: done.future,
        stop: () {},
      );
      var quitCalls = 0;

      final monitor = runExitMonitor(
        () async => tail,
        quit: () async => quitCalls++,
      );

      chunks.add('a log line');
      await Future<void>.delayed(Duration.zero);
      expect(quitCalls, 0);

      done.complete();
      await monitor;
      expect(quitCalls, 1);
    });

    test('does not quit when the tail cannot be started', () async {
      var quitCalls = 0;

      await runExitMonitor(
        () async => throw StateError('tail unavailable'),
        quit: () async => quitCalls++,
      );

      expect(quitCalls, 0);
    });

    test('quits after a failed follow when the game is already gone', () async {
      var quitCalls = 0;

      await runExitMonitor(
        () async => throw StateError('tail unavailable'),
        quit: () async => quitCalls++,
        stillRunning: () async => false,
      );

      expect(quitCalls, 1);
    });

    test(
      'keeps running after a failed follow when the game still runs',
      () async {
        var quitCalls = 0;

        await runExitMonitor(
          () async => throw StateError('tail unavailable'),
          quit: () async => quitCalls++,
          stillRunning: () async => true,
        );

        expect(quitCalls, 0);
      },
    );

    test(
      'follows again when the follow limit ends while the game runs',
      () async {
        var starts = 0;
        var checks = 0;
        var quitCalls = 0;

        await runExitMonitor(
          () async {
            starts++;
            return finishedTail();
          },
          quit: () async => quitCalls++,
          stillRunning: () async {
            checks++;
            return checks == 1;
          },
        );

        expect(starts, 2);
        expect(checks, 2);
        expect(quitCalls, 1);
      },
    );

    test('does not quit when the running state cannot be read', () async {
      var quitCalls = 0;

      await runExitMonitor(
        () async => finishedTail(),
        quit: () async => quitCalls++,
        stillRunning: () async => throw StateError('status unavailable'),
      );

      expect(quitCalls, 0);
    });
  });

  group('launcher behavior settings', () {
    testWidgets('switches are visible, start off and persist', (tester) async {
      final container = await _pumpSettings(tester);

      expect(find.text('Launcher behavior'), findsOneWidget);
      expect(find.text('Hide launcher after start'), findsOneWidget);
      expect(find.text('Quit after the game exits'), findsOneWidget);
      expect(find.textContaining('instance menu (View logs)'), findsOneWidget);

      final hide = find.byKey(const ValueKey('settings-hide-after-launch'));
      final quit = find.byKey(const ValueKey('settings-quit-after-game-exit'));
      expect(tester.widget<Switch>(hide).value, isFalse);
      expect(tester.widget<Switch>(quit).value, isFalse);

      await tester.ensureVisible(hide);
      await tester.tap(hide);
      await tester.pumpAndSettle();
      expect(tester.widget<Switch>(hide).value, isTrue);
      expect(container.read(preferencesProvider).hideAfterLaunch, isTrue);

      await tester.ensureVisible(quit);
      await tester.tap(quit);
      await tester.pumpAndSettle();
      expect(tester.widget<Switch>(quit).value, isTrue);
      expect(container.read(preferencesProvider).quitAfterGameExit, isTrue);

      final stored = await SharedPreferences.getInstance();
      expect(stored.getBool('ui.hideAfterLaunch'), isTrue);
      expect(stored.getBool('ui.quitAfterGameExit'), isTrue);
      expect(tester.takeException(), isNull);
    });

    testWidgets('the switches are hidden on a non-desktop platform', (
      tester,
    ) async {
      await _pumpSettings(tester, desktop: false);

      expect(find.text('Launcher behavior'), findsNothing);
      expect(
        find.byKey(const ValueKey('settings-hide-after-launch')),
        findsNothing,
      );
      expect(
        find.byKey(const ValueKey('settings-quit-after-game-exit')),
        findsNothing,
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('the switches fit a narrow window', (tester) async {
      await _pumpSettings(tester, size: const Size(720, 900));

      expect(find.text('Hide launcher after start'), findsOneWidget);
      expect(find.text('Quit after the game exits'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  });
}
