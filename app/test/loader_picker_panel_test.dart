import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/loaders/loader_versions.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/instances/loader_picker_panel.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const versions = [
    LoaderVersionInfo(version: '0.19.5', latest: true),
    LoaderVersionInfo(version: '0.19.4', stable: true, recommended: true),
    LoaderVersionInfo(version: '0.18.6', stable: true),
  ];

  Future<void> pumpPanel(
    WidgetTester tester, {
    GameLoader loader = GameLoader.fabric,
    String selectedVersion = '0.19.4',
    ValueChanged<GameLoader>? onLoaderSelected,
    ValueChanged<String>? onVersionSelected,
    List<LoaderVersionInfo> items = versions,
    bool loading = false,
    String? error,
  }) async {
    tester.view.physicalSize = const Size(900, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: 600,
              child: LoaderPickerPanel(
                loaders: const [
                  GameLoader.vanilla,
                  GameLoader.fabric,
                  GameLoader.quilt,
                  GameLoader.forge,
                  GameLoader.neoforge,
                ],
                selectedLoader: loader,
                onLoaderSelected: onLoaderSelected ?? (_) {},
                versions: items,
                selectedVersion: selectedVersion,
                onVersionSelected: onVersionSelected ?? (_) {},
                loading: loading,
                error: error,
              ),
            ),
          ),
        ),
      ),
    );
    // A spinner never settles, so advance a fixed number of frames instead of
    // pumping until quiet.
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pump();
  }

  testWidgets('shows loader types beside the build list', (tester) async {
    await pumpPanel(tester);

    for (final label in ['Vanilla', 'Fabric', 'Quilt', 'Forge', 'NeoForge']) {
      expect(find.text(label), findsOneWidget);
    }
    expect(find.text('0.19.5'), findsOneWidget);
    // The selected build also appears under the active loader on the left.
    expect(find.text('0.19.4'), findsWidgets);
    expect(find.text('0.18.6'), findsOneWidget);
    expect(find.text('Latest'), findsOneWidget);
    expect(find.text('Recommended'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('selecting vanilla retracts the build column', (tester) async {
    GameLoader? picked;
    await pumpPanel(
      tester,
      onLoaderSelected: (loader) => picked = loader,
    );

    expect(find.text('0.19.5'), findsOneWidget);
    await tester.tap(find.text('Vanilla'));
    await tester.pumpAndSettle();

    expect(picked, GameLoader.vanilla);
    // The caller would pass the new loader; re-pump with vanilla to verify the
    // collapsed state (build rows and search are gone).
    await pumpPanel(
      tester,
      loader: GameLoader.vanilla,
      items: const <LoaderVersionInfo>[],
      selectedVersion: '',
    );
    expect(find.text('0.19.5'), findsNothing);
    expect(find.byType(TextField), findsNothing);
    expect(
      find.textContaining('No mod loader'),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('search filters builds and reports a miss', (tester) async {
    await pumpPanel(tester);

    await tester.enterText(find.byType(TextField), '0.18');
    await tester.pumpAndSettle();
    expect(find.text('0.18.6'), findsOneWidget);
    expect(find.text('0.19.5'), findsNothing);

    await tester.enterText(find.byType(TextField), 'nope');
    await tester.pumpAndSettle();
    expect(find.text('No builds match this search.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('selecting a build reports the version', (tester) async {
    String? selected;
    await pumpPanel(
      tester,
      onVersionSelected: (version) => selected = version,
    );

    await tester.tap(find.text('0.18.6'));
    await tester.pumpAndSettle();
    expect(selected, '0.18.6');
    expect(tester.takeException(), isNull);
  });

  testWidgets('fits a narrow panel width', (tester) async {
    await pumpPanel(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: 420,
              child: LoaderPickerPanel(
                width: 420,
                loaders: const [GameLoader.vanilla, GameLoader.fabric],
                selectedLoader: GameLoader.fabric,
                onLoaderSelected: (_) {},
                versions: versions,
                selectedVersion: '0.19.4',
                onVersionSelected: (_) {},
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));
    expect(find.text('0.19.4'), findsWidgets);
    expect(tester.takeException(), isNull);
  });

  testWidgets('shows loading and error states', (tester) async {
    await pumpPanel(tester, loading: true);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    await pumpPanel(
      tester,
      error: 'Could not load loader versions.',
      items: const <LoaderVersionInfo>[],
    );
    expect(find.text('Could not load loader versions.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
