import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/router.dart';
import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/pages/download_page.dart';
import 'package:archoera_cx/pages/resource_detail_page.dart';
import 'package:archoera_cx/services/core/resources/resource_models.dart';
import 'package:archoera_cx/stores/instances.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _fabric = GameInstance(
  id: 'fabric-1.21.1',
  name: '1.21.1 · Fabric',
  gameVersion: '1.21.1',
  loader: GameLoader.fabric,
  loaderVersion: '0.16.5',
  rootPath: '/tmp/archoera-instance',
);

const _vanilla = GameInstance(
  id: 'vanilla-1.21.1',
  name: '1.21.1 · Vanilla',
  gameVersion: '1.21.1',
  loader: GameLoader.vanilla,
  rootPath: '/tmp/archoera-vanilla',
);

const _project = ResourceProject(
  id: 'sodium',
  slug: 'sodium',
  name: 'Sodium',
  summary: 'A modern rendering engine for Minecraft.',
  kind: 'mod',
  downloads: 12345,
  follows: 678,
  author: 'jellysquid3',
  license: 'LGPL-3.0',
  categories: ['optimization'],
  loaders: ['fabric'],
  gallery: [ResourceGalleryImage(url: '')],
  body: '## Features\n\nA **fast** renderer.',
);

final _versions = <ResourceVersion>[
  ResourceVersion(
    id: 'sodium-1',
    projectId: 'sodium',
    name: 'Sodium 0.6.0',
    versionNumber: 'mc1.21.1-0.6.0',
    versionType: 'release',
    gameVersions: const ['1.21.1'],
    loaders: const ['fabric'],
    files: const [
      ResourceFile(
        fileName: 'sodium.jar',
        url: 'https://example.invalid/sodium.jar',
        size: 1048576,
        primary: true,
      ),
    ],
    dependencies: const [
      ResourceDependency(kind: 'required', projectId: 'fabric-api'),
    ],
  ),
  ResourceVersion(
    id: 'sodium-2',
    projectId: 'sodium',
    name: 'Sodium 0.5.11',
    versionNumber: 'mc1.21.1-0.5.11',
    versionType: 'beta',
    gameVersions: const ['1.21.1'],
    loaders: const ['fabric'],
  ),
];

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  Future<ProviderContainer> pumpPage(
    WidgetTester tester, {
    Size size = const Size(1280, 900),
    GameInstance? instance = _fabric,
    bool focusVersions = false,
    List<ResourceVersion>? versions,
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final container = ProviderContainer(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        selectedInstanceProvider.overrideWithValue(instance),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          theme: AppTheme.light,
          locale: const Locale('en'),
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: ToastOverlay(
            child: Scaffold(
              body: ResourceDetailPage(
                kind: 'mod',
                projectId: _project.id,
                project: _project,
                focusVersions: focusVersions,
                projectLoader: (_) async => _project,
                versionsLoader: (_) async => versions ?? _versions,
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    return container;
  }

  testWidgets('shows the project overview and versions side by side', (
    tester,
  ) async {
    await pumpPage(tester);

    expect(find.text('Sodium'), findsOneWidget);
    expect(find.text('A modern rendering engine for Minecraft.'), findsOneWidget);
    expect(find.text('Screenshots'), findsOneWidget);
    expect(find.textContaining('A fast renderer.'), findsOneWidget);
    expect(find.text('Select version'), findsOneWidget);
    expect(find.text('Minecraft 1.21.1 · Fabric'), findsOneWidget);
    expect(find.text('Sodium 0.6.0'), findsOneWidget);
    expect(find.text('Sodium 0.5.11'), findsOneWidget);

    // Wide windows put the version list in its own column, not a small pane
    // over the catalogue.
    final overview = tester.getTopLeft(find.text('Screenshots'));
    final versions = tester.getTopLeft(find.text('Select version'));
    expect(versions.dx, greaterThan(overview.dx));
    expect(tester.takeException(), isNull);
  });

  testWidgets('stacks overview and versions on a narrow window', (
    tester,
  ) async {
    await pumpPage(tester, size: const Size(760, 900));

    final overview = tester.getTopLeft(find.text('Screenshots'));
    final versions = tester.getTopLeft(find.text('Select version'));
    expect(versions.dy, greaterThan(overview.dy));
    expect(find.text('Sodium 0.6.0'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('reveals large version lists in pages', (tester) async {
    final many = [
      for (var index = 1; index <= 45; index++)
        ResourceVersion(
          id: 'version-$index',
          projectId: _project.id,
          name: 'Build $index',
          versionNumber: '1.0.$index',
          versionType: 'release',
        ),
    ];

    await pumpPage(tester, versions: many);

    // The first page stays bounded even when a project has hundreds of builds.
    expect(find.text('Build 40'), findsOneWidget);
    expect(find.text('Build 41'), findsNothing);

    final more = find.text('Load more');
    await tester.ensureVisible(more);
    await tester.tap(more);
    await tester.pumpAndSettle();

    expect(find.text('Build 41'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('keeps the install target visible and refuses a vanilla mod', (
    tester,
  ) async {
    await pumpPage(tester, instance: _vanilla);

    expect(find.text('Minecraft 1.21.1 · Vanilla'), findsOneWidget);

    await tester.tap(find.byTooltip('Install').first);
    await tester.pump();
    expect(
      find.text(
        'Select a Fabric/Forge/NeoForge/Quilt instance before installing mods.',
      ),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);

    // Let the toast timer expire before teardown.
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('opens as a page on the download branch and pops back', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final router = buildAppRouter(initialLocation: '/download');
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          coreClientProvider.overrideWithValue(null),
          routerProvider.overrideWithValue(router),
        ],
        child: const ArchoeraApp(),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(DownloadPage), findsOneWidget);

    router.push(
      '/download/project/mod/sodium?versions=1',
      extra: _project,
    );
    await tester.pumpAndSettle();

    expect(find.byType(ResourceDetailPage), findsOneWidget);
    expect(find.byType(DownloadPage), findsNothing);
    expect(
      find.byType(DownloadPage, skipOffstage: false),
      findsOneWidget,
      reason: 'the catalogue stays mounted so its search state survives',
    );

    await tester.ensureVisible(find.byTooltip('Back'));
    await tester.tap(find.byTooltip('Back'));
    await tester.pumpAndSettle();

    expect(find.byType(ResourceDetailPage), findsNothing);
    expect(find.byType(DownloadPage), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
