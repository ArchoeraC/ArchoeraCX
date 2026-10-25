/// Cross-platform check for an absolute file path.
///
/// The launcher stores paths exactly as the user picked them, so a settings
/// document may have been written on another platform (for example a Windows
/// path while inspecting a backup). Accept POSIX, UNC and drive-letter forms
/// here and leave the actual existence/ABI checks to the C# owner.
bool isAbsoluteFilePath(String path) {
  final value = path.trim();
  if (value.isEmpty) return false;
  if (value.startsWith('/')) return true;
  if (value.startsWith(r'\\')) return true;
  return RegExp(r'^[A-Za-z]:[\\/]').hasMatch(value);
}
