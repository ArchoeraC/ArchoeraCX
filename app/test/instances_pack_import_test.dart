import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/services/core/packs/pack_install_result.dart';
import 'package:archoera_cx/stores/downloads.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:file_selector_platform_interface/file_selector_platform_interface.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Records the import instead of touching the native core, so the test only
/// covers what the page does: pick a file, hand it to the queue, stay open.
class _RecordingDownloads extends DownloadsNotifier {
  final List<String> importedPaths = <String>[];
  final List<String> importedLabels = <String>[];

  @override
  Future<PackInspection?> installModpackFromFile(
    String path, {
    required String label,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    required String inspectFailedMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    importedPaths.add(path);
    importedLabels.add(label);
    return const PackInspection(
      name: 'Better MC BMC5',
      versionId: '1.0.0',
      summary: '',
      gameVersion: '1.21.1',
      loader: 'neoforge',
      loaderVersion: '21.1.176',
      fileCount: 261,
      totalBytes: 0,
      overrideFiles: 492,
      overrideBytes: 0,
      ignoredServerFiles: 0,
    );
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

Widget _app(_RecordingDownloads downloads) => ProviderScope(
  overrides: [
    coreClientProvider.overrideWithValue(null),
    downloadsProvider.overrideWith(() => downloads),
  ],
  child: const ArchoeraApp(),
);

void main() {
  setUp(() => SharedPreferences.setMockInitialValues(<String, Object>{}));

  testWidgets('importing a pack queues it without a blocking dialog', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final downloads = _RecordingDownloads();
    FileSelectorPlatform.instance = _FakeFileSelector('/tmp/BetterMC.mrpack');

    await tester.pumpWidget(_app(downloads));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(OutlinedButton, 'Import pack'));
    await tester.pumpAndSettle();

    expect(downloads.importedPaths, ['/tmp/BetterMC.mrpack']);
    expect(downloads.importedLabels, ['BetterMC.mrpack']);
    // The regression: a modal progress dialog used to own the whole install.
    expect(find.byType(AlertDialog), findsNothing);
    // The queue confirms with the pack's real name, not the file name.
    expect(find.textContaining('Better MC BMC5'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('cancelling the picker starts nothing', (tester) async {
    final downloads = _RecordingDownloads();
    FileSelectorPlatform.instance = _FakeFileSelector(null);

    await tester.pumpWidget(_app(downloads));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(OutlinedButton, 'Import pack'));
    await tester.pumpAndSettle();

    expect(downloads.importedPaths, isEmpty);
    expect(find.byType(AlertDialog), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
