/// Heap preview returned by `launch.memory`.
///
/// The native module owns the algorithm; the UI only renders the numbers and
/// turns [warningCode] into localized text.
final class LaunchMemoryPlan {
  const LaunchMemoryPlan({
    required this.mode,
    required this.minMb,
    required this.maxMb,
    required this.recommendedMb,
    required this.systemTotalMb,
    required this.modCount,
    this.systemAvailableMb,
    this.warningCode,
    this.warning,
  });

  /// `auto` or `manual`.
  final String mode;

  /// `-Xms` value.
  final int minMb;

  /// `-Xmx` value.
  final int maxMb;

  /// Heap automatic sizing would pick, even in manual mode.
  final int recommendedMb;

  /// Installed RAM, 0 when the platform does not report it.
  final int systemTotalMb;

  /// Available RAM, null when unknown.
  final int? systemAvailableMb;

  /// Enabled mods counted in the instance.
  final int modCount;

  /// `LOW_MEMORY`, `BELOW_RECOMMENDED`, `OVER_SYSTEM`, `OVER_AVAILABLE`,
  /// `SYSTEM_LOW_MEMORY` or null.
  final String? warningCode;

  /// English fallback message for logs; the UI localizes [warningCode].
  final String? warning;

  bool get isAuto => mode == 'auto';

  bool get hasWarning => warningCode != null;

  /// True when a manual heap is below what automatic sizing recommends.
  bool get isBelowRecommended => maxMb < recommendedMb;

  static LaunchMemoryPlan? tryParse(Object? value) {
    if (value is! Map) return null;
    final maxMb = _int(value['maxMb']);
    final recommended = _int(value['recommendedMb']);
    if (maxMb == null || recommended == null) return null;
    return LaunchMemoryPlan(
      mode: value['mode'] == 'manual' ? 'manual' : 'auto',
      minMb: _int(value['minMb']) ?? maxMb,
      maxMb: maxMb,
      recommendedMb: recommended,
      systemTotalMb: _int(value['systemTotalMb']) ?? 0,
      systemAvailableMb: _int(value['systemAvailableMb']),
      modCount: _int(value['modCount']) ?? 0,
      warningCode: _string(value['warningCode']),
      warning: _string(value['warning']),
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }

  static String? _string(Object? value) {
    final text = value?.toString().trim();
    return text == null || text.isEmpty ? null : text;
  }
}
