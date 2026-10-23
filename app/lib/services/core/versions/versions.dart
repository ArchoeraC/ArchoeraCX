import 'dart:convert';

import '../core_transport.dart';
import 'vanilla_install_plan.dart';
import 'version_models.dart';

/// Vanilla game-version domain binding for the native core: `version.list`,
/// `version.plan` and `version.commit`.
mixin CoreClientVersions on CoreTransport {
  /// Read Mojang's version manifest through the C# module.
  Future<MojangVersionList> listGameVersions({
    bool includeSnapshots = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'version.list';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'includeSnapshots': includeSnapshots,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return MojangVersionList(
      latestRelease: asString(data['latestRelease']),
      latestSnapshot: asString(data['latestSnapshot']),
      items: List<MojangGameVersion>.unmodifiable(
        asList(data['items'])
            .map(MojangGameVersion.tryParse)
            .whereType<MojangGameVersion>(),
      ),
    );
  }

  /// Build a vanilla game-file plan rooted at the instance's install root.
  Future<VanillaInstallPlan> planVanillaInstall({
    required String root,
    required String versionId,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'version.plan';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
      }),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final plan = asMap(data['plan'], methodName);
    final id = asString(data['versionId']);
    final versionJson = asString(data['versionJson']);
    final installRoot = asString(data['installRoot']);
    if (id == null || versionJson == null || installRoot == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an incomplete plan');
    }
    return VanillaInstallPlan(
      plan: plan,
      versionId: id,
      versionJson: versionJson,
      installRoot: installRoot,
    );
  }

  /// Write a downloaded vanilla version JSON into the install root.
  Future<void> commitVanillaVersion({
    required String root,
    required String versionId,
    required String versionJson,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'version.commit';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
        'versionJson': versionJson,
      }),
      onProgress: onProgress,
      label: label,
    );
    decodeEnvelope(jsonText, fallbackMethod: methodName);
  }
}
