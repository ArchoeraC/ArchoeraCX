// Renders the launcher pages to PNG for the web presence.
//
// WSLg cannot grab the real window, so the promotional shots come from the
// widget tree itself: the same pages, theme tokens and bundled fonts, driven
// with the sample instances the shell already falls back to when the native
// core is absent, plus a display-only account. Run it with:
//
//   flutter test tool/screenshots/capture_test.dart
//
// Output defaults to `../site/assets/shots` and can be redirected with
// `ARCHOERACX_SHOT_DIR`. Only the launcher owns these pixels; the marketing
// page must not copy third-party screenshots.

import 'dart:io';
import 'dart:ui' as ui;

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/models/account.dart';
import 'package:archoera_cx/stores/accounts.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _window = Size(1440, 900);
const _contact = Size(1000, 700);

/// A display-only account for the marketing shots.
///
/// The shell otherwise shows its "add an account" empty state, which reads as
/// a clipped hint in a 1440×900 screenshot. No token, skin URL or network
/// request is involved; this is the same metadata the C# store would return.
class _SampleAccounts extends AccountsNotifier {
  @override
  Future<List<Account>> build() async => const [
    Account(
      id: 'sample-steve',
      kind: AccountKind.microsoft,
      displayName: 'Steve',
      active: true,
    ),
  ];
}

Future<void> _loadFonts() async {
  final sans = FontLoader('NotoSansSC')
    ..addFont(rootBundle.load('assets/fonts/NotoSansSC-Medium.otf'));
  await sans.load();
  final icons = FontLoader('AppIcons')
    ..addFont(rootBundle.load('assets/fonts/AppIcons.ttf'));
  await icons.load();
}

/// Pumps a page long enough for async image decodes to land.
///
/// Loader marks and the default skin are `Image.asset`s; their decode runs on
/// the real event loop, so a fake-async `pumpAndSettle` would capture empty
/// boxes.
Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 20; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 300)),
  );
  await tester.pump();
}

Future<void> _capture(
  WidgetTester tester,
  String name, {
  required String theme,
  String? nav,
  Size size = _window,
}) async {
  // This is a widget-test harness that lives under tool/ so normal test runs
  // do not write site assets; the checker still flags the test-only helper.
  // ignore: invalid_use_of_visible_for_testing_member
  SharedPreferences.setMockInitialValues(<String, Object>{
    'ui.locale': 'zh',
    'ui.themeMode': theme,
  });
  tester.view.physicalSize = size * 2;
  tester.view.devicePixelRatio = 2;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  final key = GlobalKey();
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        accountsProvider.overrideWith(_SampleAccounts.new),
      ],
      child: RepaintBoundary(key: key, child: const ArchoeraApp()),
    ),
  );
  await _settle(tester);

  if (nav != null) {
    await tester.tap(find.byKey(ValueKey('nav-$nav')));
    await _settle(tester);
  }

  final boundary = tester.renderObject<RenderRepaintBoundary>(find.byKey(key));
  final bytes = await tester.runAsync(() async {
    final image = await boundary.toImage(pixelRatio: 1);
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    image.dispose();
    return data!.buffer.asUint8List();
  });

  final directory = Directory(
    Platform.environment['ARCHOERACX_SHOT_DIR'] ?? '../site/assets/shots',
  );
  await tester.runAsync(() async {
    await directory.create(recursive: true);
    final file = File('${directory.path}/$name.png');
    await file.writeAsBytes(bytes!);
    // ignore: avoid_print
    print('wrote ${file.path}');
  });
}

void main() {
  setUpAll(_loadFonts);

  testWidgets('home light', (tester) async {
    await _capture(tester, 'home-light', theme: 'light');
  });

  testWidgets('home dark', (tester) async {
    await _capture(tester, 'home-dark', theme: 'dark');
  });

  testWidgets('instances light', (tester) async {
    await _capture(
      tester,
      'instances-light',
      size: _contact,
      theme: 'light',
      nav: '/instances',
    );
  });

  testWidgets('instances dark', (tester) async {
    await _capture(
      tester,
      'instances-dark',
      size: _contact,
      theme: 'dark',
      nav: '/instances',
    );
  });

  testWidgets('settings light', (tester) async {
    await _capture(
      tester,
      'settings-light',
      size: _contact,
      theme: 'light',
      nav: '/settings',
    );
  });

  testWidgets('settings dark', (tester) async {
    await _capture(
      tester,
      'settings-dark',
      size: _contact,
      theme: 'dark',
      nav: '/settings',
    );
  });
}
