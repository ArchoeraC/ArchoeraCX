/// Physical memory reported by the native system module.
final class SystemMemoryInfo {
  const SystemMemoryInfo({
    required this.totalMb,
    required this.availableMb,
    this.platform,
  });

  final int totalMb;
  final int availableMb;
  final String? platform;

  bool get isKnown => totalMb > 0;

  static SystemMemoryInfo? tryParse(Object? value) {
    if (value is! Map) return null;
    final total = _int(value['totalMb']);
    final available = _int(value['availableMb']);
    if (total == null || available == null) return null;
    return SystemMemoryInfo(
      totalMb: total,
      availableMb: available,
      platform: value['platform']?.toString(),
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }
}
