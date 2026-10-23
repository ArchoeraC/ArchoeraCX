import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/stores/downloads.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:archoera_cx/widgets/instances/loader_install_dialog.dart';
import 'package:file_selector_platform_interface/file_selector_platform_interface.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Records the loader install instead of touching the native core, so the test
/// only covers what the dialog does: validate, hand off, close.
class _RecordingDownloads extends DownloadsNotifier {
  final List<String> kinds = <String>[];
  final List<String> installerPaths = <String>[];
  final List<bool?> shares = <bool?>[];
  CoreException? failure;

  @override
  Future<void> installLoader({
    required GameInstance instance,
    required String kind,
    required String gameVersion,
    required String loaderVersion,
    required String installerPath,
    bool? shareLoaderFiles,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    kinds.add(kind);
    installerPaths.add(installerPath);
    shares.add(shareLoaderFiles);
    final failure = this.failure;
    if (failure != null) throw failure;
  }
}

class _FakeFileSelector extends FileSelectorPlatform {
  _FakeFileSelector(this.path);

  final String? path;

  @override
  Future<XFile?> openFile({
    List<XTypeGroup>? acceptedTypeGroups,
    String? initialDirectory,
    String? confirmButtonText,
  }) async => path == null ? null : XFile(path!);
}

const _instance = GameInstance(
  id: 'inst-1',
  name: 'AutumnTown',
  gameVersion: '1.21.1',
  loader: GameLoader.neoforge,
  loaderVersion: '21.1.176',
  rootPath: '/tmp/archoera/AutumnTown',
);

Widget _host(_RecordingDownloads downloads) => ProviderScope(
  overrides: [
    coreClientProvider.overrideWithValue(null),
    downloadsProvider.overrideWith(() => downloads),
  ],
  child: MaterialApp(
    theme: AppTheme.dark,
    locale: const Locale('en'),
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    builder: (context, child) => ToastOverlay(child: child!),
    home: Builder(
      builder: (context) => Scaffold(
        body: Center(
          child: TextButton(
            onPressed: () => showLoaderInstallDialog(context, _instance),
            child: const Text('open'),
          ),
        ),
      ),
    ),
  ),
);

void main() {
  setUp(() => SharedPreferences.setMockInitialValues(<String, Object>{}));

  testWidgets('installing a loader queues it and closes the dialog', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final downloads = _RecordingDownloads();
    FileSelectorPlatform.instance = _FakeFileSelector(
      '/tmp/forge-installer.jar',
    );

    await tester.pumpWidget(_host(downloads));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    // The dialog prefills the loader version from the instance, so only the
    // installer file has to be chosen before installing.
    await tester.tap(find.widgetWithText(OutlinedButton, 'Choose installer'));
    await tester.pumpAndSettle();
    expect(find.text('/tmp/forge-installer.jar'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'Download and commit'));
    await tester.pumpAndSettle();

    expect(downloads.kinds, ['neoforge']);
    expect(downloads.installerPaths, ['/tmp/forge-installer.jar']);
    // Untouched, the dialog defers to the global preference (null), which the
    // queue resolves against the current settings.
    expect(downloads.shares, [null]);
    // The regression: the dialog used to stay open for the whole download.
    expect(find.byType(LoaderInstallDialog), findsNothing);
    expect(find.textContaining('queued'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('the sharing checkbox overrides the default for one install', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final downloads = _RecordingDownloads();
    FileSelectorPlatform.instance = _FakeFileSelector(
      '/tmp/forge-installer.jar',
    );

    await tester.pumpWidget(_host(downloads));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    final sharing = find.byType(CheckboxListTile);
    expect(sharing, findsOneWidget);
    expect(tester.widget<CheckboxListTile>(sharing).value, isTrue);

    await tester.ensureVisible(sharing);
    await tester.tap(sharing);
    await tester.pumpAndSettle();
    expect(tester.widget<CheckboxListTile>(sharing).value, isFalse);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Choose installer'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Download and commit'));
    await tester.pumpAndSettle();

    expect(downloads.shares, [false]);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('a missing Java keeps the dialog open with a reason', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final downloads = _RecordingDownloads()
      ..failure = const CoreException(
        status: 1,
        code: 'JAVA_MISSING',
        message: 'no java',
      );
    FileSelectorPlatform.instance = _FakeFileSelector(
      '/tmp/forge-installer.jar',
    );

    await tester.pumpWidget(_host(downloads));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(OutlinedButton, 'Choose installer'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Download and commit'));
    await tester.pumpAndSettle();

    expect(find.byType(LoaderInstallDialog), findsOneWidget);
    expect(
      find.textContaining('Java'),
      findsWidgets,
      reason: 'the pre-flight failure must be explained in the dialog',
    );
    expect(tester.takeException(), isNull);
  });
}
