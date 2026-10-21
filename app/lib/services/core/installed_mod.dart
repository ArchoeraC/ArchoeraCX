/// A mod file owned by one instance. The C# instance module is the only
/// component that touches the filesystem.
final class InstalledMod {
  const InstalledMod({
    required this.fileName,
    required this.name,
    required this.enabled,
    required this.size,
    this.modifiedAt,
  });

  /// Name on disk, including `.disabled` when disabled.
  final String fileName;

  /// Logical file name without `.disabled`.
  final String name;
  final bool enabled;
  final int size;
  final DateTime? modifiedAt;

  static InstalledMod? tryParse(Object? value) {
    if (value is! Map) return null;
    final fileName = value['fileName']?.toString();
    final name = value['name']?.toString();
    if (fileName == null || fileName.isEmpty || name == null || name.isEmpty) {
      return null;
    }
    return InstalledMod(
      fileName: fileName,
      name: name,
      enabled: value['enabled'] == true,
      size: _int(value['size']) ?? 0,
      modifiedAt: DateTime.tryParse(value['modifiedAt']?.toString() ?? ''),
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }
}
