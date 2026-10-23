import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/game_instance.dart';
import '../services/core/core_client.dart';
import '../services/core/java_runtime_plan.dart';
import '../services/core/resource_models.dart';
import '../services/core/task_update.dart';
import '../widgets/common/toast.dart';
import 'instances.dart';
import 'preferences.dart';
import 'providers.dart';
import 'tasks.dart';

/// Methods that actually move bytes to disk.
///
/// The download page surfaces these in its queue strip; planning/bookkeeping
/// calls (`version.plan`, `loader.check` …) stay in the task center only.
const _downloadMethods = <String>{
  'download.execute',
  'modpack.install',
  'modpack.installLocal',
};

bool isDownloadTaskMethod(String method) => _downloadMethods.contains(method);

/// Running transfers, in task-center order.
final activeDownloadsProvider = Provider<List<TaskUpdate>>((ref) {
  final tasks = ref.watch(taskCenterProvider);
  return [
    for (final task in tasks)
      if (!task.isTerminal && isDownloadTaskMethod(task.method)) task,
  ];
});

/// Loader facet Modrinth expects for an instance.
String loaderSlug(GameLoader loader) => switch (loader) {
  GameLoader.fabric => 'fabric',
  GameLoader.forge => 'forge',
  GameLoader.neoforge => 'neoforge',
  GameLoader.quilt => 'quilt',
  _ => 'fabric',
};

/// Starts installs in the background.
///
/// Every entry point returns as soon as the native task exists; progress,
/// cancellation and history belong to the task center (`taskCenterProvider`).
/// Completion and failure are reported with toasts, so no page ever has to hold
/// a blocking dialog open.
final downloadsProvider = NotifierProvider<DownloadsNotifier, void>(
  DownloadsNotifier.new,
);

class DownloadsNotifier extends Notifier<void> {
  @override
  void build() {}

  /// Vanilla client, libraries, assets and the version JSON for [instance].
  Future<void> installGameFiles(
    GameInstance instance, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    final plan = await client.planVanillaInstall(
      root: instance.rootPath,
      versionId: instance.gameVersion,
      label: instance.gameVersion,
    );

    _detach(
      () async {
        final result = await client.executeDownload(
          plan.plan,
          label: '${instance.name} · ${instance.gameVersion}',
        );
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Game download failed (${result.status}).',
          );
        }
        await client.commitVanillaVersion(
          root: instance.rootPath,
          versionId: plan.versionId,
          versionJson: plan.versionJson,
          label: instance.gameVersion,
        );
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Mod, resource pack or shader into [instance] (dependencies included).
  Future<void> installResource({
    required ResourceProject project,
    required ResourceVersion version,
    required GameInstance instance,
    required String kind,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    final plan = await client.planModrinthDownload(
      projectId: project.id,
      versionId: version.id,
      kind: kind,
      gameVersion: instance.gameVersion,
      loader: loaderSlug(instance.loader),
      shaderLoader: kind == 'shader' ? 'iris' : null,
      includeDependencies: true,
      root: instance.rootPath,
    );

    _detach(
      () async {
        final result = await client.executeDownload(plan, label: project.name);
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Download failed (${result.status}).',
          );
        }
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Modrinth modpack: the backend creates the instance, installs the loader
  /// and merges overrides. The new instance is selected when it lands.
  Future<void> installModpackFromModrinth({
    required String projectId,
    required String versionId,
    required String label,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    _detach(
      () async {
        final result = await client.installModpackFromModrinth(
          projectId: projectId,
          versionId: versionId,
          label: label,
        );
        await ref.read(instancesProvider.notifier).sync();
        ref
            .read(selectedInstanceIdProvider.notifier)
            .select(result.instance.id);
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Installs a Mojang Java runtime (e.g. Java 21) under the data root.
  ///
  /// The plan is built first so a missing component fails before the UI claims
  /// the download started; the transfer itself runs in the background.
  Future<void> installJavaRuntime(
    int major, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    JavaRuntimePlan plan;
    try {
      plan = await client.planJavaRuntime(major: major);
    } catch (error) {
      toast('$failureMessage\n$error', type: ToastType.error);
      return;
    }

    await _runDetached(
      () async {
        final result = await client.executeDownload(
          plan.plan,
          label: 'Java $major',
        );
        if (!result.ok) {
          throw StateError(result.stderr?.trim().isNotEmpty == true
              ? result.stderr!.trim()
              : 'Java runtime download failed (${result.status}).');
        }
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Runs [job] to completion without anyone awaiting it.
  ///
  /// `unawaited` keeps the analyzer honest: the task center owns the live
  /// state, so losing the future is intentional.
  void _detach(
    Future<void> Function() job, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) {
    unawaited(
      _runDetached(
        job,
        successMessage: successMessage,
        failureMessage: failureMessage,
        cancelledMessage: cancelledMessage,
      ),
    );
  }

  /// Same as [_detach] but the caller may await completion (for follow-up
  /// work such as rescanning for the newly installed runtime).
  Future<void> _runDetached(
    Future<void> Function() job, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) {
    return (() async {
      try {
        await job();
        toast(successMessage, type: ToastType.success);
        _notifyDesktop(successMessage);
      } on CoreException catch (error) {
        final cancelled = error.code == 'E_CANCELLED';
        toast(
          cancelled ? cancelledMessage : '$failureMessage\n$error',
          type: cancelled ? ToastType.info : ToastType.error,
        );
      } catch (error) {
        toast('$failureMessage\n$error', type: ToastType.error);
      }
    })();
  }

  /// Optional desktop notification; the user can switch it off in Settings.
  void _notifyDesktop(String body) {
    if (!ref.read(preferencesProvider).downloadNotifications) return;
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    unawaited(
      client.showSystemNotification(title: 'ArchoeraCX', body: body),
    );
  }

  CoreClient _client() {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError('The native core is not connected.');
    }
    return client;
  }
}
