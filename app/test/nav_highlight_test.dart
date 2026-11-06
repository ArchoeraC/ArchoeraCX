import 'dart:convert';
import 'dart:io';

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _highlight = ValueKey('nav-highlight');

/// 1×1 transparent PNG, enough for `FileImage` to decode in widget tests.
final _pixelPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8A'
  'AQUBAScY42YAAAAASUVORK5CYII=',
);

Future<void> pumpApp(
  WidgetTester tester, {
  Size size = const Size(1440, 900),
}) => pumpAppWith(tester, const <String, Object>{}, size: size);

/// Pumps the app with [preferences] preloaded, for appearance-dependent tests.
Future<void> pumpAppWith(
  WidgetTester tester,
  Map<String, Object> preferences, {
  Size size = const Size(1440, 900),
}) async {
  SharedPreferences.setMockInitialValues(preferences);
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [coreClientProvider.overrideWithValue(null)],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
}

Rect plate(WidgetTester tester) => tester.getRect(find.byKey(_highlight));

Rect item(WidgetTester tester, String path) =>
    tester.getRect(find.byKey(ValueKey('nav-$path')));

Matcher rectCloseTo(Rect expected) => predicate<Rect>(
  (actual) =>
      (actual.left - expected.left).abs() < 0.5 &&
      (actual.top - expected.top).abs() < 0.5 &&
      (actual.width - expected.width).abs() < 0.5 &&
      (actual.height - expected.height).abs() < 0.5,
  'a rect within half a pixel of $expected',
);

void main() {
  testWidgets('the home destination is never highlighted', (tester) async {
    await pumpApp(tester);

    // The dashboard is the canvas itself: no plate under the home tab.
    expect(find.byKey(_highlight), findsNothing);

    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();
    expect(find.byKey(_highlight), findsOneWidget);
    expect(plate(tester), rectCloseTo(item(tester, '/instances')));

    await tester.tap(find.byKey(const ValueKey('nav-/')));
    await tester.pumpAndSettle();
    expect(find.byKey(_highlight), findsNothing);
  });

  testWidgets('all destinations share one vertical baseline', (tester) async {
    await pumpApp(tester);

    final paths = [
      '/',
      '/instances',
      '/stats',
      '/download',
      '/accounts',
      '/wiki',
      '/settings',
    ];
    final first = item(tester, paths.first);
    for (final path in paths.skip(1)) {
      final rect = item(tester, path);
      expect(
        rect.top,
        first.top,
        reason: 'nav item $path is vertically offset',
      );
      expect(
        rect.height,
        first.height,
        reason: 'nav item $path has a different height',
      );
    }

    // The home item only differs by not being highlighted; its icon and label
    // must still sit on the same line as every other destination.
    final homeIcon = tester.getRect(
      find.descendant(
        of: find.byKey(const ValueKey('nav-/')),
        matching: find.byType(Icon),
      ),
    );
    final instancesIcon = tester.getRect(
      find.descendant(
        of: find.byKey(const ValueKey('nav-/instances')),
        matching: find.byType(Icon),
      ),
    );
    expect(homeIcon.top, instancesIcon.top);
    expect(homeIcon.height, instancesIcon.height);
  });

  testWidgets('the home destination never shows hover or press ink', (
    tester,
  ) async {
    await pumpApp(tester);

    InkWell inkFor(String path) => tester.widget<InkWell>(
      find.descendant(
        of: find.byKey(ValueKey('nav-$path')),
        matching: find.byType(InkWell),
      ),
    );

    final home = inkFor('/');
    expect(home.hoverColor, Colors.transparent);
    expect(home.highlightColor, Colors.transparent);
    expect(home.splashColor, Colors.transparent);

    final other = inkFor('/instances');
    expect(other.hoverColor, isNot(Colors.transparent));
    expect(other.highlightColor, isNull);
    expect(other.splashColor, isNull);
  });

  testWidgets('a wallpaper swaps the hover plate for a light wash', (
    tester,
  ) async {
    await pumpApp(tester);

    InkWell inkFor(String path) => tester.widget<InkWell>(
      find.descendant(
        of: find.byKey(ValueKey('nav-$path')),
        matching: find.byType(InkWell),
      ),
    );

    // Solid canvas: the home footer is the page colour, so other destinations
    // keep the regular hover feedback.
    expect(inkFor('/').hoverColor, Colors.transparent);
    expect(inkFor('/instances').hoverColor, isNot(Colors.transparent));

    // Wallpaper: the chrome is translucent, so every item (home included) gets
    // a light wash instead of the opaque panel colour.
    final directory = Directory.systemTemp.createTempSync('archoera-nav');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));
    await tester.pumpWidget(const SizedBox.shrink());
    await pumpAppWith(tester, <String, Object>{
      'ui.appearanceStyle': 'image',
      'ui.backgroundImage': image.path,
    });
    for (final path in const ['/', '/instances', '/settings']) {
      final ink = inkFor(path);
      expect(ink.hoverColor, isNot(Colors.transparent));
      expect(ink.hoverColor!.a, lessThan(0.2));
      expect(ink.highlightColor, isNot(Colors.transparent));
    }
  });

  testWidgets('a wallpaper frosts the bar on every page but home', (
    tester,
  ) async {
    const glass = ValueKey('command-bar-glass');
    await pumpApp(tester);
    expect(find.byKey(glass), findsNothing);

    final directory = Directory.systemTemp.createTempSync('archoera-glass');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));
    await tester.pumpWidget(const SizedBox.shrink());
    await pumpAppWith(tester, <String, Object>{
      'ui.appearanceStyle': 'image',
      'ui.backgroundImage': image.path,
    });

    // The home footer floats directly on the photo, so it adds no blur layer.
    expect(find.byKey(glass), findsNothing);

    await tester.tap(find.byKey(const ValueKey('nav-/settings')));
    await tester.pumpAndSettle();
    expect(find.byKey(glass), findsOneWidget);

    // The plate stays translucent on purpose: pushing it towards opaque turns
    // the frosted chrome back into a solid slab over the wallpaper.
    final plate = tester.widget<AnimatedContainer>(
      find
          .descendant(
            of: find.byKey(glass),
            matching: find.byType(AnimatedContainer),
          )
          .first,
    );
    final decoration = plate.decoration! as BoxDecoration;
    expect(decoration.color!.a, closeTo(0.52, 0.02));
  });

  testWidgets('the accent plate slides between destinations', (tester) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();

    final start = item(tester, '/instances');
    final target = item(tester, '/accounts');
    expect(plate(tester), rectCloseTo(start));

    await tester.tap(find.byKey(const ValueKey('nav-/accounts')));
    await tester.pump();
    await tester.pump();
    expect(tester.takeException(), isNull);

    await tester.pump(const Duration(milliseconds: 60));
    final mid = plate(tester);
    expect(mid.left, greaterThan(start.left));
    expect(mid.left, lessThan(target.left));
    expect(mid, isNot(rectCloseTo(target)));

    await tester.pumpAndSettle();
    expect(plate(tester), rectCloseTo(target));
  });

  testWidgets('the plate tracks the compact icon-only row', (tester) async {
    await pumpApp(tester, size: const Size(700, 800));

    // Compact navigation hides the labels; the hit target is icon + padding.
    expect(
      find.descendant(
        of: find.byKey(const ValueKey('nav-/instances')),
        matching: find.byType(Text),
      ),
      findsNothing,
    );
    expect(find.byKey(_highlight), findsNothing);

    await tester.tap(find.byKey(const ValueKey('nav-/settings')));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(plate(tester), rectCloseTo(item(tester, '/settings')));
    expect(plate(tester).width, lessThan(80));
  });

  testWidgets('a relayout snaps the plate instead of sliding', (tester) async {
    // Start compact (icons only), then widen: the labels appear, every nav item
    // changes width and the plate must land on the new rect in that frame.
    await pumpApp(tester, size: const Size(700, 800));
    await tester.tap(find.byKey(const ValueKey('nav-/settings')));
    await tester.pumpAndSettle();
    expect(plate(tester).width, lessThan(80));

    tester.view.physicalSize = const Size(1440, 900);
    await tester.pump();
    await tester.pump();

    expect(plate(tester), rectCloseTo(item(tester, '/settings')));
    expect(plate(tester).width, greaterThan(100));

    await tester.pumpAndSettle();
    expect(plate(tester), rectCloseTo(item(tester, '/settings')));
  });
}
